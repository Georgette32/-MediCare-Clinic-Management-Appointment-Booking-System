using MediCare.Domain.Entities;

namespace MediCare.Application.Interfaces.Persistence;

public interface ISpecializationRepository
{
    Task<IReadOnlyList<Specialization>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Specialization>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Specialization?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<int> CountDoctorsAsync(Guid specializationId, CancellationToken cancellationToken = default);

    Task AddAsync(Specialization specialization, CancellationToken cancellationToken = default);

    void Remove(Specialization specialization);
}
