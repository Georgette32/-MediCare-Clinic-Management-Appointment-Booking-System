using MediCare.Application.Common;
using MediCare.Application.DTOs.Specializations;

namespace MediCare.Application.Interfaces.Services;

public interface ISpecializationService
{
    Task<IReadOnlyList<SpecializationDto>> GetAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpecializationDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<Result<SpecializationDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<SpecializationDto>> CreateAsync(CreateSpecializationRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(UpdateSpecializationRequest request, CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
