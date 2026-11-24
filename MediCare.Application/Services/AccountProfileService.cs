using System.Data;
using System.Net.Mail;
using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

public sealed record AccountProfileView(
    Guid UserId,
    string FullName,
    string UserName,
    IReadOnlyList<string> Roles,
    bool IsPatient,
    string? PhoneNumber,
    string? Email);

public sealed record AccountProfileResult(bool Succeeded, string Message);

public interface IAccountProfileService
{
    Task<AccountProfileView?> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<AccountProfileResult> ChangeFullNameAsync(Guid userId, string? fullName, CancellationToken cancellationToken);
    /// <summary>Patients only. Blank input removes the email. Staff emails are their usernames and cannot be changed here.</summary>
    Task<AccountProfileResult> ChangeEmailAsync(Guid userId, string? email, CancellationToken cancellationToken);
}

public sealed class AccountProfileService(
    IIdentityProfileStore identity,
    IAccountProfileRepository profiles,
    IIdentityAccountStore accounts,
    IUnitOfWork unitOfWork) : IAccountProfileService
{
    public const int MaxEmailLength = 256;
    private const string PatientRole = "Patient";
    private const string DoctorRole = "Doctor";
    private const string DuplicateEmailMessage = "An account with this email already exists.";

    public async Task<AccountProfileView?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identity.FindAsync(userId, cancellationToken);
        if (user is null)
            return null;

        var isPatient = user.Roles.Contains(PatientRole);
        var patient = isPatient ? await profiles.GetPatientByApplicationUserIdAsync(userId, cancellationToken) : null;
        var fullName = patient?.FullName ?? user.FullName;
        return new AccountProfileView(
            user.UserId,
            fullName,
            user.UserName,
            user.Roles,
            isPatient,
            isPatient ? user.UserName : null,
            user.Email);
    }

    public async Task<AccountProfileResult> ChangeFullNameAsync(Guid userId, string? fullName, CancellationToken cancellationToken)
    {
        var name = (fullName ?? string.Empty).Trim();
        if (name.Length == 0)
            return new AccountProfileResult(false, "Enter your full name.");
        if (name.Length > Patient.MaxFullNameLength)
            return new AccountProfileResult(false, $"The full name cannot exceed {Patient.MaxFullNameLength} characters.");

        var user = await identity.FindAsync(userId, cancellationToken);
        if (user is null)
            return new AccountProfileResult(false, "The account was not found.");

        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var identityResult = await identity.SetFullNameAsync(userId, name, cancellationToken);
        if (!identityResult.Succeeded)
            return new AccountProfileResult(false, string.Join(" ", identityResult.Errors));

        // Patients and doctors keep a display name on their profile row; it is updated in the same transaction.
        if (user.Roles.Contains(PatientRole))
            (await profiles.GetPatientByApplicationUserIdAsync(userId, cancellationToken))?.ChangeFullName(name);
        if (user.Roles.Contains(DoctorRole))
            (await profiles.GetDoctorByApplicationUserIdAsync(userId, cancellationToken))?.ChangeFullName(name);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AccountProfileResult(true, "Your name was updated.");
    }

    public async Task<AccountProfileResult> ChangeEmailAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        var user = await identity.FindAsync(userId, cancellationToken);
        if (user is null)
            return new AccountProfileResult(false, "The account was not found.");
        if (!user.Roles.Contains(PatientRole))
            return new AccountProfileResult(false, "Staff email addresses are their usernames and cannot be changed here.");

        var normalized = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (normalized is not null && (normalized.Length > MaxEmailLength || !IsValidEmail(normalized)))
            return new AccountProfileResult(false, "Enter a valid email address.");

        if (string.Equals(user.Email, normalized, StringComparison.OrdinalIgnoreCase) || (user.Email is null && normalized is null))
            return new AccountProfileResult(true, normalized is null ? "No email was set." : "Your email is unchanged.");

        // The user's own current email would be reported as taken, so only other holders are checked here.
        if (normalized is not null && await accounts.EmailExistsAsync(normalized, cancellationToken))
            return new AccountProfileResult(false, DuplicateEmailMessage);

        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var result = await identity.SetEmailAsync(userId, normalized, cancellationToken);
        if (!result.Succeeded)
            return new AccountProfileResult(false, result.Errors.Count > 0 ? string.Join(" ", result.Errors) : DuplicateEmailMessage);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AccountProfileResult(true, normalized is null ? "Your email was removed." : "Your email was saved.");
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            return new MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
