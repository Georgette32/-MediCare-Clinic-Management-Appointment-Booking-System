using MediCare.Domain.Entities;

namespace MediCare.Application.Interfaces.Persistence;

public interface IPatientRepository
{
    Task<PatientProfile?> GetByUserIdAsync(Guid userId, bool track = true, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task AddAsync(PatientProfile profile, CancellationToken cancellationToken = default);
}
