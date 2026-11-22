using MediCare.Domain.Entities;

namespace MediCare.Application.Interfaces.Persistence;

public interface IPrescriptionRepository
{
    /// <summary>
    /// Loads a prescription together with its items.
    /// </summary>
    Task<Prescription?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the patient's prescriptions (with items) that are not deleted, newest first.
    /// </summary>
    Task<IReadOnlyList<Prescription>> GetByPatientAsync(Guid patientProfileId, CancellationToken cancellationToken = default);

    Task AddAsync(Prescription prescription, CancellationToken cancellationToken = default);
}
