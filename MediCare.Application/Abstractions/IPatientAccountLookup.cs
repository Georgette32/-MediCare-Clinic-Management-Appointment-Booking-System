namespace MediCare.Application.Abstractions;

/// <summary>Maps a patient profile to its sign-in account. Read-only.</summary>
public interface IPatientAccountLookup
{
    Task<Guid?> FindApplicationUserIdAsync(Guid patientId, CancellationToken cancellationToken);
}
