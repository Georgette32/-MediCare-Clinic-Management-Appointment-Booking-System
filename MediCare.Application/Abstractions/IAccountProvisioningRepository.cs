using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IAccountProvisioningRepository
{
    Task<bool> PatientPhoneExistsAsync(string canonicalPhone, CancellationToken cancellationToken);
    Task<bool> SpecializationExistsAsync(Guid specializationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SpecializationChoice>> GetSpecializationsAsync(CancellationToken cancellationToken);
    Task AddPatientAsync(Patient patient, CancellationToken cancellationToken);
    Task AddDoctorAsync(Doctor doctor, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record SpecializationChoice(Guid Id, string Name);
