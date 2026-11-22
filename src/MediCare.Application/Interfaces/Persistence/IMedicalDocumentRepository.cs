using MediCare.Domain.Entities;

namespace MediCare.Application.Interfaces.Persistence;

public interface IMedicalDocumentRepository
{
    Task<MedicalDocument?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the patient's documents that are not deleted, newest first.
    /// </summary>
    Task<IReadOnlyList<MedicalDocument>> GetByPatientAsync(Guid patientProfileId, CancellationToken cancellationToken = default);

    Task AddAsync(MedicalDocument document, CancellationToken cancellationToken = default);
}
