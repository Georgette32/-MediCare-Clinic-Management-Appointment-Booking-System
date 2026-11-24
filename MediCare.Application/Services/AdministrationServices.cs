using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

public interface ISpecializationAdministrationService
{
    Task<IReadOnlyList<SpecializationAdminRow>> ListAsync(CancellationToken cancellationToken);
    Task<ManagementResult> CreateAsync(string name, string? description, CancellationToken cancellationToken);
    Task<ManagementResult> UpdateAsync(Guid specializationId, string name, string? description, CancellationToken cancellationToken);
    Task<ManagementResult> DeleteAsync(Guid specializationId, CancellationToken cancellationToken);
}

/// <summary>Admin maintenance of the specialization taxonomy. Delete is refused while any doctor references the entry.</summary>
public sealed class SpecializationAdministrationService(
    ISpecializationAdministrationRepository specializations,
    IUnitOfWork unitOfWork) : ISpecializationAdministrationService
{
    public Task<IReadOnlyList<SpecializationAdminRow>> ListAsync(CancellationToken cancellationToken) =>
        specializations.ListAsync(cancellationToken);

    public Task<ManagementResult> CreateAsync(string name, string? description, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var specialization = new Specialization(Guid.NewGuid(), name, description);
            if (await specializations.NameExistsAsync(specialization.Name, null, ct))
                throw new InvalidOperationException($"A specialization named '{specialization.Name}' already exists.");
            await specializations.AddAsync(specialization, ct);
            return $"Specialization '{specialization.Name}' created.";
        }, cancellationToken);

    public Task<ManagementResult> UpdateAsync(Guid specializationId, string name, string? description, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var specialization = await specializations.GetTrackedAsync(specializationId, ct)
                ?? throw new InvalidOperationException("The specialization was not found.");
            var trimmedName = (name ?? string.Empty).Trim();
            if (trimmedName.Length > 0 && await specializations.NameExistsAsync(trimmedName, specializationId, ct))
                throw new InvalidOperationException($"A specialization named '{trimmedName}' already exists.");
            specialization.Update(name, description);
            return $"Specialization '{specialization.Name}' updated.";
        }, cancellationToken);

    public Task<ManagementResult> DeleteAsync(Guid specializationId, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var specialization = await specializations.GetTrackedAsync(specializationId, ct)
                ?? throw new InvalidOperationException("The specialization was not found.");
            var doctorCount = await specializations.CountDoctorsAsync(specializationId, ct);
            if (doctorCount > 0)
                throw new InvalidOperationException(
                    $"'{specialization.Name}' is used by {doctorCount} doctor(s) and cannot be deleted. Move those doctors to another specialization first.");
            specializations.Remove(specialization);
            return $"Specialization '{specialization.Name}' deleted.";
        }, cancellationToken);

    private async Task<ManagementResult> RunAsync(Func<CancellationToken, Task<string>> change, CancellationToken cancellationToken)
    {
        try
        {
            var message = await unitOfWork.ExecuteSerializableWithRetryAsync(change, cancellationToken);
            return ManagementResult.Success(message);
        }
        catch (InvalidOperationException exception)
        {
            return ManagementResult.Failure(exception.Message);
        }
        catch (ArgumentException exception)
        {
            var message = exception.Message;
            var index = message.IndexOf(" (Parameter", StringComparison.Ordinal);
            return ManagementResult.Failure(index > 0 ? message[..index] : message);
        }
    }
}

public interface IStaffAccountService
{
    Task<IReadOnlyList<StaffAccountRow>> ListAsync(CancellationToken cancellationToken);
    Task<ManagementResult> SetActiveAsync(Guid targetUserId, bool isActive, Guid actorUserId, CancellationToken cancellationToken);
    /// <summary>What deactivating this doctor account would cancel, for the confirm page. Read-only.</summary>
    Task<DoctorDeactivationPreview> PreviewDoctorDeactivationAsync(Guid doctorUserId, CancellationToken cancellationToken);
}

/// <summary>
/// Activates or deactivates Doctor and Receptionist accounts. Deactivating a doctor also hides the profile from discovery
/// and booking. Admin accounts and the acting Admin's own account cannot be deactivated here.
/// </summary>
public sealed class StaffAccountService(
    IStaffAccountRepository staff,
    IUnitOfWork unitOfWork,
    IDoctorDeactivationService doctorDeactivation) : IStaffAccountService
{
    public Task<IReadOnlyList<StaffAccountRow>> ListAsync(CancellationToken cancellationToken) =>
        staff.ListStaffAsync(cancellationToken);

    public Task<DoctorDeactivationPreview> PreviewDoctorDeactivationAsync(Guid doctorUserId, CancellationToken cancellationToken) =>
        doctorDeactivation.PreviewAsync(doctorUserId, cancellationToken);

    public async Task<ManagementResult> SetActiveAsync(Guid targetUserId, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!isActive && targetUserId == actorUserId)
            return ManagementResult.Failure("You cannot deactivate your own account.");

        var role = await staff.GetRoleNameAsync(targetUserId, cancellationToken);
        if (role is not ("Doctor" or "Receptionist"))
            return ManagementResult.Failure("Only Doctor and Receptionist accounts can be activated or deactivated here.");

        if (!isActive && role == "Doctor")
            return await doctorDeactivation.DeactivateAsync(targetUserId, cancellationToken);

        try
        {
            var updated = await unitOfWork.ExecuteSerializableWithRetryAsync(
                ct => staff.SetAccountActiveAsync(targetUserId, isActive, ct), cancellationToken);
            if (!updated)
                return ManagementResult.Failure("The account was not found.");
        }
        catch (InvalidOperationException exception)
        {
            return ManagementResult.Failure(exception.Message);
        }

        return ManagementResult.Success(isActive ? "Account reactivated." : "Account deactivated.");
    }
}

public sealed record ClinicSettingsView(int? MinimumConsultationSampleSize, DateTimeOffset? UpdatedAtUtc, Guid? UpdatedByUserId);

public interface IClinicSettingsService
{
    Task<ClinicSettingsView> GetAsync(CancellationToken cancellationToken);
    Task<ManagementResult> SetMinimumSampleSizeAsync(int? value, Guid actorUserId, CancellationToken cancellationToken);
}

/// <summary>Owns the clinic settings row. Also serves the expected-duration engine as IClinicSettingsProvider.</summary>
public sealed class ClinicSettingsService(
    IClinicSettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClinicClock clock) : IClinicSettingsService, IClinicSettingsProvider
{
    public async Task<int?> GetMinimumConsultationSampleSizeAsync(CancellationToken cancellationToken) =>
        (await settings.GetAsync(cancellationToken))?.MinimumConsultationSampleSize;

    public async Task<ClinicSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        var row = await settings.GetAsync(cancellationToken);
        return row is null
            ? new ClinicSettingsView(null, null, null)
            : new ClinicSettingsView(row.MinimumConsultationSampleSize, row.UpdatedAtUtc, row.UpdatedByUserId);
    }

    public async Task<ManagementResult> SetMinimumSampleSizeAsync(int? value, Guid actorUserId, CancellationToken cancellationToken)
    {
        // Validated before any row is created, so a refused value never leaves a partial change behind.
        if (value is not null && value.Value <= 0)
            return ManagementResult.Failure("The minimum consultation sample size must be a positive whole number, or empty to clear it.");

        try
        {
            await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
            {
                var row = await settings.GetAsync(ct);
                if (row is null)
                {
                    row = ClinicSettings.CreateEmpty(clock.UtcNow);
                    await settings.AddAsync(row, ct);
                }
                row.SetMinimumConsultationSampleSize(value, actorUserId, clock.UtcNow);
                return true;
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            var message = exception.Message;
            var index = message.IndexOf(" (Parameter", StringComparison.Ordinal);
            return ManagementResult.Failure(index > 0 ? message[..index] : message);
        }

        return ManagementResult.Success(value is null
            ? "Minimum sample size cleared. The configured appointment duration will be used."
            : $"Minimum consultation sample size set to {value}.");
    }
}
