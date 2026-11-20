
using MediCare.Application.DTOs.Analytics;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Services;

public interface IAnalyticsService
{
    Task<AppointmentAnalyticsDto> GetAppointmentAnalyticsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorPerformanceDto>> GetDoctorPerformanceAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
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
