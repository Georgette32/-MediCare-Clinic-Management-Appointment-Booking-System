
using MediCare.Application.DTOs.Analytics;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Services;

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;

    public AnalyticsService(
        IAppointmentRepository appointments,
        IDoctorRepository doctors)
    {
        _appointments = appointments;
        _doctors = doctors;
    }

    public async Task<AppointmentAnalyticsDto> GetAppointmentAnalyticsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(startDateUtc, endDateUtc);

        var total = await _appointments.CountByDateRangeAsync(
            startDateUtc, endDateUtc, null, cancellationToken);

        var completed = await _appointments.CountByDateRangeAsync(
            startDateUtc, endDateUtc, AppointmentStatus.Completed, cancellationToken);

        var cancelled = await _appointments.CountByDateRangeAsync(
            startDateUtc, endDateUtc, AppointmentStatus.Cancelled, cancellationToken);

        var noShow = await _appointments.CountByDateRangeAsync(
            startDateUtc, endDateUtc, AppointmentStatus.NoShow, cancellationToken);

        return new AppointmentAnalyticsDto
        {
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            TotalAppointments = total,
            CompletedAppointments = completed,
            CancelledAppointments = cancelled,
            NoShowAppointments = noShow,
            CompletionRate = CalculateRate(completed, total),
            CancellationRate = CalculateRate(cancelled, total),
            NoShowRate = CalculateRate(noShow, total)
        };
    }

    public async Task<IReadOnlyList<DoctorPerformanceDto>> GetDoctorPerformanceAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(startDateUtc, endDateUtc);

        return await _doctors.GetPerformanceAsync(
            startDateUtc, endDateUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<PeakHoursDto>> GetPeakHoursAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(startDateUtc, endDateUtc);

        return await _appointments.GetPeakHoursAsync(
            startDateUtc, endDateUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<SpecializationAnalyticsDto>>
        GetPopularSpecializationsAsync(
            DateTime startDateUtc,
            DateTime endDateUtc,
            CancellationToken cancellationToken = default)
    {
        ValidateDateRange(startDateUtc, endDateUtc);

        return await _appointments.GetPopularSpecializationsAsync(
            startDateUtc, endDateUtc, cancellationToken);
    }

    private static double CalculateRate(int count, int total)
    {
        return total == 0
            ? 0
            : Math.Round((double)count / total * 100, 2);
    }

    private static void ValidateDateRange(DateTime startDateUtc, DateTime endDateUtc)
    {
        if (startDateUtc >= endDateUtc)
            throw new ArgumentException("Start date must be earlier than end date.");
    }
}