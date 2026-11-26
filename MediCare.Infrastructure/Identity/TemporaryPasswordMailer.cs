using MediCare.Infrastructure.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MediCare.Infrastructure.Identity;

public enum TemporaryPasswordMailStatus { Sent, NoEmail, Unverified, Failed }

public sealed record TemporaryPasswordMail(TemporaryPasswordMailStatus Status, string? MaskedEmail = null);

public interface ITemporaryPasswordMailer
{
    /// <summary>Emails a freshly reset temporary password to the account's address. Never throws for a mail failure.</summary>
    Task<TemporaryPasswordMail> SendAsync(Guid userId, string temporaryPassword, string siteUrl, CancellationToken cancellationToken);
}

public sealed class TemporaryPasswordMailer(
    UserManager<ApplicationUser> users,
    IEmailSender sender,
    ILogger<TemporaryPasswordMailer> logger) : ITemporaryPasswordMailer
{
    public async Task<TemporaryPasswordMail> SendAsync(Guid userId, string temporaryPassword, string siteUrl, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return new TemporaryPasswordMail(TemporaryPasswordMailStatus.NoEmail);
        // A sign-up that never entered its code may have a mistyped address that belongs to someone else.
        if (EmailCodeRules.IsPending(user))
            return new TemporaryPasswordMail(TemporaryPasswordMailStatus.Unverified);

        try
        {
            await sender.SendAsync(TemporaryPasswordEmail.Build(user.FullName, temporaryPassword, user.Email, siteUrl), cancellationToken);
            return new TemporaryPasswordMail(TemporaryPasswordMailStatus.Sent, EmailCodeRules.Mask(user.Email));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The reset already happened; the password is still on screen for reception to hand over.
            logger.LogError(exception, "Emailing the temporary password for account {UserId} failed.", user.Id);
            return new TemporaryPasswordMail(TemporaryPasswordMailStatus.Failed);
        }
    }
}
