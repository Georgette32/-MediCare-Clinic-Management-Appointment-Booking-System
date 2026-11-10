using MediCare.Application.Common;
using MediCare.Application.DTOs.Doctors;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Interfaces.Persistence;

public interface IDoctorRepository
{
    Task<DoctorProfile?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default);

    Task<DoctorProfile?> GetByUserIdAsync(Guid userId, bool track = true, CancellationToken cancellationToken = default);

    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> LicenseNumberExistsAsync(string licenseNumber, Guid? excludeDoctorId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<DoctorProfile>> GetPagedAsync(DoctorQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorProfile>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(DoctorApprovalStatus? status = null, CancellationToken cancellationToken = default);

    Task AddAsync(DoctorProfile profile, CancellationToken cancellationToken = default);
}
