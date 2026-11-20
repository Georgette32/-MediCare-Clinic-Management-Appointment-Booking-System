
using MediCare.Application.DTOs.Analytics;
using MediCare.Domain.Enums;

namespace MediCare.Application.Interfaces.Persistence;

public interface IAppointmentRepository
{
    Task<int> CountAsync(
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<int> CountUpcomingAsync(
        CancellationToken cancellationToken = default);

    Task<int> CountByDateRangeAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeakHoursDto>> GetPeakHoursAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpecializationAnalyticsDto>> GetPopularSpecializationsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);
}
