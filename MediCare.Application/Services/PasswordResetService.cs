using System.Data;
using MediCare.Application.Abstractions;

namespace MediCare.Application.Services;

/// <summary>
/// Outcome of a password reset. The temporary password is present only on success and must be shown once, never stored or logged.
/// </summary>
public sealed record PasswordResetResult(
    bool Succeeded,
    string Message,
    string? TemporaryPassword = null,
    Guid? UserId = null,
    string? UserName = null,
    string? FullName = null,
    string? Role = null)
{
    public static PasswordResetResult Failure(string message) => new(false, message);
}

public interface IPasswordResetService
{
    /// <summary>Admin resets a Doctor or Receptionist password. Never an Admin account, never the actor's own account.</summary>
    Task<PasswordResetResult> ResetStaffPasswordAsync(Guid targetUserId, Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>Reception or Admin resets a Patient account password, identified by the patient profile id.</summary>
    Task<PasswordResetResult> ResetPatientPasswordAsync(Guid patientId, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class PasswordResetService(
    IIdentityAccountStore identity,
    IPatientAccountLookup patients,
    IUnitOfWork unitOfWork) : IPasswordResetService
{
    private const string AdminRole = "Admin";
    private const string DoctorRole = "Doctor";
    private const string ReceptionistRole = "Receptionist";
    private const string PatientRole = "Patient";

    public async Task<PasswordResetResult> ResetStaffPasswordAsync(Guid targetUserId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var actor = await identity.FindAccountAsync(actorUserId, cancellationToken);
        if (actor is null || !actor.Roles.Contains(AdminRole))
            return PasswordResetResult.Failure("Only an Admin can reset staff passwords.");
        if (targetUserId == actorUserId)
            return PasswordResetResult.Failure("You cannot reset your own password here. Use Change password instead.");

        var target = await identity.FindAccountAsync(targetUserId, cancellationToken);
        if (target is null)
            return PasswordResetResult.Failure("The account was not found.");
        if (target.Roles.Contains(AdminRole) || target.Roles.Contains(PatientRole))
            return PasswordResetResult.Failure("Only Doctor and Receptionist accounts can be reset here.");

        var role = target.Roles.Contains(DoctorRole) ? DoctorRole
            : target.Roles.Contains(ReceptionistRole) ? ReceptionistRole
            : null;
        if (role is null)
            return PasswordResetResult.Failure("Only Doctor and Receptionist accounts can be reset here.");
        if (!target.IsActive)
            return PasswordResetResult.Failure("Reactivate the account before resetting its password.");

        return await ResetAsync(target, role, cancellationToken);
    }

    public async Task<PasswordResetResult> ResetPatientPasswordAsync(Guid patientId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var actor = await identity.FindAccountAsync(actorUserId, cancellationToken);
        if (actor is null || !(actor.Roles.Contains(AdminRole) || actor.Roles.Contains(ReceptionistRole)))
            return PasswordResetResult.Failure("Only Reception or Admin can reset patient passwords.");

        var userId = await patients.FindApplicationUserIdAsync(patientId, cancellationToken);
        if (userId is null)
            return PasswordResetResult.Failure("The patient was not found.");

        var target = await identity.FindAccountAsync(userId.Value, cancellationToken);
        if (target is null || !target.Roles.Contains(PatientRole))
            return PasswordResetResult.Failure("The patient's sign-in account was not found.");
        if (!target.IsActive)
            return PasswordResetResult.Failure("This patient's account is inactive and cannot be reset.");

        return await ResetAsync(target, PatientRole, cancellationToken);
    }

    private async Task<PasswordResetResult> ResetAsync(IdentityAccountSnapshot target, string role, CancellationToken cancellationToken)
    {
        var temporaryPassword = TemporaryPasswordGenerator.Create();
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var reset = await identity.ResetPasswordAsync(target.UserId, temporaryPassword, cancellationToken);
        if (!reset.Succeeded)
            return PasswordResetResult.Failure("The password could not be reset. Try again.");

        await transaction.CommitAsync(cancellationToken);
        return new PasswordResetResult(
            Succeeded: true,
            Message: "Password reset.",
            TemporaryPassword: temporaryPassword,
            UserId: target.UserId,
            UserName: target.UserName,
            FullName: target.FullName,
            Role: role);
    }
}
