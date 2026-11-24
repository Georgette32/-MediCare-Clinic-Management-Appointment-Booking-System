using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public sealed record SpecializationAdminRow(Guid Id, string Name, string? Description, int DoctorCount);

/// <summary>Admin maintenance of the specialization taxonomy.</summary>
public interface ISpecializationAdministrationRepository
{
    Task<IReadOnlyList<SpecializationAdminRow>> ListAsync(CancellationToken cancellationToken);
    Task<Specialization?> GetTrackedAsync(Guid specializationId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string name, Guid? exceptSpecializationId, CancellationToken cancellationToken);
    Task<int> CountDoctorsAsync(Guid specializationId, CancellationToken cancellationToken);
    Task AddAsync(Specialization specialization, CancellationToken cancellationToken);
    void Remove(Specialization specialization);
}

public sealed record StaffAccountRow(
    Guid UserId,
    string FullName,
    string UserName,
    string Role,
    bool IsActive,
    Guid? DoctorId,
    string? Specialization);

/// <summary>Staff account listing and activation. Only Doctor and Receptionist accounts are exposed here.</summary>
public interface IStaffAccountRepository
{
    Task<IReadOnlyList<StaffAccountRow>> ListStaffAsync(CancellationToken cancellationToken);

    /// <summary>The application role name of the account (Admin, Doctor, Receptionist or Patient), or null when the account is missing.</summary>
    Task<string?> GetRoleNameAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sets the account flag and, for a doctor profile, the Doctor.IsActive flag. Persisted by the unit of work.</summary>
    Task<bool> SetAccountActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);
}
