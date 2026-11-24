using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

/// <summary>Identity-side view of an account. Application code never sees Identity types.</summary>
public sealed record IdentityProfileSnapshot(Guid UserId, string UserName, string? Email, string FullName, bool IsActive, IReadOnlyList<string> Roles);

public sealed record IdentityProfileOperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static IdentityProfileOperationResult Success() => new(true, []);
    public static IdentityProfileOperationResult Failure(params string[] errors) => new(false, errors);
}

/// <summary>Reads and updates the Identity user behind an account (name and email only).</summary>
public interface IIdentityProfileStore
{
    Task<IdentityProfileSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken);
    Task<IdentityProfileOperationResult> SetFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken);
    /// <summary>Sets or clears (null) the email. A unique-index violation is returned as a failure, not thrown.</summary>
    Task<IdentityProfileOperationResult> SetEmailAsync(Guid userId, string? email, CancellationToken cancellationToken);
}

/// <summary>Profile rows (Patient and Doctor) linked to an Identity user. Changes are saved by the unit of work.</summary>
public interface IAccountProfileRepository
{
    Task<Patient?> GetPatientByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken);
    Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken);
}
