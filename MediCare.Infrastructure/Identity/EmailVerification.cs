using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using MediCare.Infrastructure.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MediCare.Infrastructure.Identity;

public enum EmailCodeSendStatus { Sent, CoolingDown, Failed, NotPending }

public sealed record EmailCodeSend(EmailCodeSendStatus Status, int CooldownSeconds = 0);

public enum EmailCodeCheckStatus { Confirmed, Wrong, Expired, Locked, NotPending }

public sealed record EmailCodeCheck(EmailCodeCheckStatus Status, int TriesLeft = 0);

public sealed record EmailChange(bool Succeeded, string? Error, EmailCodeSend? Send = null);

/// <summary>What the verify page needs to draw itself. <see cref="ExpiresAtUtc"/> is null when no usable code is outstanding.</summary>
public sealed record EmailVerificationState(string MaskedEmail, DateTimeOffset? ExpiresAtUtc, int CooldownSeconds, int TriesLeft);

/// <summary>
/// The 6-digit email code rules, on the account row only (no I/O): 10-minute lifetime, 60-second resend cooldown,
/// 5 wrong tries end the code, only a hash is stored, a new code replaces the old one.
/// </summary>
public static class EmailCodeRules
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    public static bool IsPending(ApplicationUser user) => user.RequiresEmailVerification && !user.EmailConfirmed;

    public static int CooldownSecondsLeft(ApplicationUser user, DateTimeOffset now) =>
        user.EmailCodeSentAtUtc is { } sent && now - sent < Cooldown
            ? (int)Math.Ceiling((Cooldown - (now - sent)).TotalSeconds)
            : 0;

    /// <summary>True while the current code can still be entered.</summary>
    public static bool HasLiveCode(ApplicationUser user, DateTimeOffset now) =>
        user.EmailCodeHash is not null && user.EmailCodeExpiresAtUtc > now && user.EmailCodeFailedAttempts < MaxAttempts;

    /// <summary>Creates a new code, stores its hash and timing on the account and returns the plain code for the email.</summary>
    public static string Issue(ApplicationUser user, DateTimeOffset now)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        user.EmailCodeHash = Hash(user.Id, code);
        user.EmailCodeSentAtUtc = now;
        user.EmailCodeExpiresAtUtc = now + Lifetime;
        user.EmailCodeFailedAttempts = 0;
        return code;
    }

    /// <summary>Checks a typed code and updates the account (attempt count, or confirmation and clearing the code).</summary>
    public static EmailCodeCheck Check(ApplicationUser user, string? typed, DateTimeOffset now)
    {
        if (!IsPending(user))
            return new EmailCodeCheck(EmailCodeCheckStatus.NotPending);
        if (user.EmailCodeHash is null || user.EmailCodeFailedAttempts >= MaxAttempts)
            return new EmailCodeCheck(user.EmailCodeHash is null ? EmailCodeCheckStatus.Expired : EmailCodeCheckStatus.Locked);
        if (user.EmailCodeExpiresAtUtc is not { } expires || expires <= now)
            return new EmailCodeCheck(EmailCodeCheckStatus.Expired);

        var digits = new string((typed ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var matches = digits.Length == 6 && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(user.Id, digits)),
            Encoding.ASCII.GetBytes(user.EmailCodeHash));
        if (matches)
        {
            user.EmailConfirmed = true;
            Clear(user);
            return new EmailCodeCheck(EmailCodeCheckStatus.Confirmed);
        }

        user.EmailCodeFailedAttempts++;
        var left = MaxAttempts - user.EmailCodeFailedAttempts;
        return left > 0
            ? new EmailCodeCheck(EmailCodeCheckStatus.Wrong, left)
            : new EmailCodeCheck(EmailCodeCheckStatus.Locked);
    }

    public static void Clear(ApplicationUser user)
    {
        user.EmailCodeHash = null;
        user.EmailCodeExpiresAtUtc = null;
        user.EmailCodeFailedAttempts = 0;
    }

    /// <summary>SHA-256 of the account id and the code, as 64 hex characters. The id makes equal codes hash differently.</summary>
    public static string Hash(Guid userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{code}")));

    /// <summary>"nour.mohamed@gmail.com" becomes "nour.m••••@gmail.com": enough to recognise, not enough to harvest.</summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;
        var at = email.LastIndexOf('@');
        if (at <= 0)
            return email;
        var local = email[..at];
        var keep = Math.Clamp(local.Length / 2, 1, 6);
        return local[..keep] + "••••" + email[at..];
    }

    public static bool IsValidAddress(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
            return false;
        try
        {
            var parsed = new MailAddress(email.Trim());
            return parsed.Address == email.Trim() && parsed.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public interface IEmailVerificationService
{
    EmailVerificationState GetState(ApplicationUser user);
    Task<EmailCodeSend> SendCodeAsync(ApplicationUser user, string siteUrl, CancellationToken cancellationToken);
    Task<EmailCodeCheck> VerifyAsync(ApplicationUser user, string? code);
    Task<EmailChange> ChangeEmailAsync(ApplicationUser user, string? email, string siteUrl, CancellationToken cancellationToken);
}

/// <summary>Persists <see cref="EmailCodeRules"/> through Identity and sends the branded email.</summary>
public sealed class EmailVerificationService(
    UserManager<ApplicationUser> users,
    IEmailSender sender,
    TimeProvider time,
    TimeZoneInfo clinicTimeZone,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    private DateTimeOffset Now => time.GetUtcNow();

    public EmailVerificationState GetState(ApplicationUser user)
    {
        var now = Now;
        return new EmailVerificationState(
            EmailCodeRules.Mask(user.Email),
            EmailCodeRules.HasLiveCode(user, now) ? user.EmailCodeExpiresAtUtc : null,
            EmailCodeRules.CooldownSecondsLeft(user, now),
            Math.Max(EmailCodeRules.MaxAttempts - user.EmailCodeFailedAttempts, 0));
    }

    public async Task<EmailCodeSend> SendCodeAsync(ApplicationUser user, string siteUrl, CancellationToken cancellationToken)
    {
        if (!EmailCodeRules.IsPending(user) || string.IsNullOrWhiteSpace(user.Email))
            return new EmailCodeSend(EmailCodeSendStatus.NotPending);

        var now = Now;
        var wait = EmailCodeRules.CooldownSecondsLeft(user, now);
        if (wait > 0)
            return new EmailCodeSend(EmailCodeSendStatus.CoolingDown, wait);

        var code = EmailCodeRules.Issue(user, now);
        var saved = await users.UpdateAsync(user);
        if (!saved.Succeeded)
            return new EmailCodeSend(EmailCodeSendStatus.Failed);

        try
        {
            var expiresLocal = TimeZoneInfo.ConvertTime(user.EmailCodeExpiresAtUtc!.Value, clinicTimeZone);
            await sender.SendAsync(VerificationEmail.Build(user.FullName, code, expiresLocal, user.Email, siteUrl), cancellationToken);
            return new EmailCodeSend(EmailCodeSendStatus.Sent, (int)EmailCodeRules.Cooldown.TotalSeconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The account and the code stay; clearing the send time lets the patient press Resend straight away.
            logger.LogError(exception, "Sending the verification email for account {UserId} failed.", user.Id);
            user.EmailCodeSentAtUtc = null;
            await users.UpdateAsync(user);
            return new EmailCodeSend(EmailCodeSendStatus.Failed);
        }
    }

    public async Task<EmailCodeCheck> VerifyAsync(ApplicationUser user, string? code)
    {
        var before = user.EmailCodeFailedAttempts;
        var result = EmailCodeRules.Check(user, code, Now);
        if (result.Status is EmailCodeCheckStatus.Confirmed || user.EmailCodeFailedAttempts != before)
        {
            var saved = await users.UpdateAsync(user);
            if (!saved.Succeeded)
                throw new InvalidOperationException("The verification result could not be saved.");
        }
        return result;
    }

    public async Task<EmailChange> ChangeEmailAsync(ApplicationUser user, string? email, string siteUrl, CancellationToken cancellationToken)
    {
        if (!EmailCodeRules.IsPending(user))
            return new EmailChange(false, "This account is already confirmed.");
        var address = email?.Trim() ?? string.Empty;
        if (!EmailCodeRules.IsValidAddress(address))
            return new EmailChange(false, "Enter a valid email address, like name@example.com.");
        if (string.Equals(address, user.Email, StringComparison.OrdinalIgnoreCase))
            return new EmailChange(false, "That's the address we already sent the code to.");
        var wait = EmailCodeRules.CooldownSecondsLeft(user, Now);
        if (wait > 0)
            return new EmailChange(false, $"Wait {wait} seconds, then try the new address.");
        if (await users.FindByEmailAsync(address) is { } owner && owner.Id != user.Id)
            return new EmailChange(false, "An account with this email already exists.");

        var previous = (user.Email, user.NormalizedEmail);
        user.Email = address;
        user.NormalizedEmail = users.NormalizeEmail(address);
        EmailCodeRules.Clear(user);
        IdentityResult saved;
        try
        {
            saved = await users.UpdateAsync(user);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Another account took the address between the check and the save (unique email index).
            saved = IdentityResult.Failed();
        }
        if (!saved.Succeeded)
        {
            (user.Email, user.NormalizedEmail) = previous;
            return new EmailChange(false, "An account with this email already exists.");
        }

        return new EmailChange(true, null, await SendCodeAsync(user, siteUrl, cancellationToken));
    }
}
