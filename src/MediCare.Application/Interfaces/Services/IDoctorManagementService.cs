using MediCare.Application.Common;
using MediCare.Application.DTOs.Doctors;

namespace MediCare.Application.Interfaces.Services;

public interface IDoctorManagementService
{
    Task<PagedResult<DoctorListItemDto>> GetDoctorsAsync(DoctorQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorListItemDto>> GetPendingDoctorsAsync(CancellationToken cancellationToken = default);

    Task<Result<DoctorDetailsDto>> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken = default);

    Task<Result<DoctorDetailsDto>> GetDoctorByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ApproveAsync(Guid administratorId, Guid doctorId, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(Guid administratorId, Guid doctorId, string reason, CancellationToken cancellationToken = default);

    Task<Result> SuspendAsync(Guid administratorId, Guid doctorId, string reason, CancellationToken cancellationToken = default);

    Task<Result> SetAccountActiveAsync(Guid administratorId, Guid doctorId, bool isActive, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid administratorId, UpdateDoctorRequest request, CancellationToken cancellationToken = default);
}
