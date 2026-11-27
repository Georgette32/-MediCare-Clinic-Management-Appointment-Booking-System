using MediCare.Application.Abstractions;
using MediCare.Application.Analytics;
using MediCare.Application.Common;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Application.Services;

public sealed class AnalyticsService(
    IAnalyticsRepository repository,
    IClinicClock clinicClock,
    TimeZoneInfo clinicZone,
    IOptions<ExpectedDurationOptions> expectedDurationOptions,
    IAnalyticsResultCache cache) : IAnalyticsService
{
    /// <summary>Closed (historical) results are reused for a short time. Live results are never cached.</summary>
    public static readonly TimeSpan HistoricalCacheTimeToLive = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _serviceCap = TimeSpan.FromMinutes(expectedDurationOptions.Value.MaxDurationMinutesCap);

    public async Task<DoctorAnalytics?> GetDoctorAnalyticsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        EnsureHistoricalRange(fromDate, toDate);

        var key = $"analytics:doctor:{doctorId:N}:{fromDate:yyyyMMdd}:{toDate:yyyyMMdd}";
        if (cache.TryGet<DoctorAnalytics>(key, out var cached) && cached is not null)
            return cached;

        var doctorIds = new[] { doctorId };
        var doctors = await repository.GetDoctorsAsync(doctorIds, cancellationToken);
        if (doctors.Count == 0)
            return null;

        var snapshot = await LoadAsync(doctors, fromDate, toDate, includeAppointments: true, cancellationToken);
        var result = AnalyticsCalculator.Calculate(snapshot, RequestFor(fromDate, toDate, cutoffUtc: null))
            .Single(item => item.DoctorId == doctorId);

        cache.Set(key, result, HistoricalCacheTimeToLive);
        return result;
    }

    public async Task<DoctorAnalytics> GetOwnDoctorAnalyticsAsync(Guid applicationUserId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var doctorId = await repository.GetDoctorIdForApplicationUserAsync(applicationUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The signed-in user has no linked doctor profile.");

        return await GetDoctorAnalyticsAsync(doctorId, fromDate, toDate, cancellationToken)
            ?? throw new UnauthorizedAccessException("The linked doctor profile was not found.");
    }

    public async Task<ClinicAnalyticsReport> GetClinicAnalyticsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        EnsureHistoricalRange(fromDate, toDate);

        var key = $"analytics:clinic:all:{fromDate:yyyyMMdd}:{toDate:yyyyMMdd}";
        if (cache.TryGet<ClinicAnalyticsReport>(key, out var cached) && cached is not null)
            return cached;

        var doctors = await repository.GetDoctorsAsync(null, cancellationToken);
        var snapshot = await LoadAsync(doctors, fromDate, toDate, includeAppointments: true, cancellationToken);
        var all = AnalyticsCalculator.Calculate(snapshot, RequestFor(fromDate, toDate, cutoffUtc: null));

        // Doctors with no activity in the period are not shown in the table.
        var rows = all
            .Where(item => item.Metrics.ArrivalsCount > 0 || item.Metrics.OpenHours > 0 || item.Metrics.NoShowDenominator > 0)
            .OrderBy(item => item.DoctorName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var report = new ClinicAnalyticsReport(fromDate, toDate, rows, Totals(all));
        cache.Set(key, report, HistoricalCacheTimeToLive);
        return report;
    }

    public async Task<LiveOperationsReport> GetLiveAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var now = clinicClock.UtcNow;

        var doctors = (await repository.GetDoctorsAsync(doctorIds, cancellationToken))
            .Where(item => item.IsActive)
            .OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (doctors.Length == 0)
            return new LiveOperationsReport(today, now, []);

        var snapshot = await LoadAsync(doctors, today, today, includeAppointments: false, cancellationToken);
        // Recent consultation lengths steady today's mean until today has a few consultations of its own.
        var history = await repository.GetServiceHistoryAsync(
            doctors.Select(item => item.DoctorId).ToArray(),
            today.AddDays(-ExpectedDurationOptions.TrailingDaysWindow),
            today.AddDays(-1),
            cancellationToken);
        var request = RequestFor(today, today, cutoffUtc: now) with
        {
            ServicePriors = AnalyticsCalculator.BuildServicePriors(history, _serviceCap)
        };
        var metrics = AnalyticsCalculator.Calculate(snapshot, request);

        var rows = metrics.Select(item =>
        {
            var visits = snapshot.Visits.Where(visit => visit.DoctorId == item.DoctorId && !visit.ClinicUnavailable).ToArray();
            var waiting = visits.Where(visit => visit.Status == QueueEntryStatus.Waiting).ToArray();
            double? longest = waiting.Length > 0
                ? waiting.Max(visit => (now - visit.ArrivedAt).TotalMinutes)
                : null;

            return new LiveDoctorRow(
                item.DoctorId,
                item.DoctorName,
                waiting.Length,
                visits.Count(visit => visit.Status == QueueEntryStatus.Called),
                visits.Count(visit => visit.Status == QueueEntryStatus.InConsultation),
                longest,
                item.Metrics);
        }).ToArray();

        return new LiveOperationsReport(today, now, rows);
    }

    private async Task<AnalyticsSnapshot> LoadAsync(
        IReadOnlyList<AnalyticsDoctor> doctors,
        DateOnly fromDate,
        DateOnly toDate,
        bool includeAppointments,
        CancellationToken cancellationToken)
    {
        // One query per table for the doctor set and period. Sequential awaits: the DbContext is not thread-safe.
        var ids = doctors.Select(item => item.DoctorId).ToArray();
        var visits = await repository.GetVisitsAsync(ids, fromDate, toDate, cancellationToken);
        var appointments = includeAppointments
            ? await repository.GetAppointmentsAsync(ids, fromDate, toDate, cancellationToken)
            : [];
        var hours = await repository.GetActiveWorkingHoursAsync(ids, cancellationToken);
        var holidays = await repository.GetHolidaysAsync(ids, fromDate, toDate, cancellationToken);
        var extensions = await repository.GetApprovedExtensionsAsync(ids, fromDate, toDate, cancellationToken);
        var breaks = await repository.GetBreaksAsync(ids, fromDate, toDate, cancellationToken);
        // Snapshots in force by the period's end: a day's hours come from the state recorded for that day.
        var snapshots = await repository.GetWorkingHourSnapshotsAsync(ids, toDate, cancellationToken);

        return new AnalyticsSnapshot(doctors, visits, appointments, hours, holidays, extensions, breaks, snapshots);
    }

    private AnalyticsRequest RequestFor(DateOnly fromDate, DateOnly toDate, DateTimeOffset? cutoffUtc) =>
        new(fromDate, toDate, clinicClock.Today, cutoffUtc, _serviceCap, clinicZone, clinicClock.ToUtc);

    private void EnsureHistoricalRange(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw new ArgumentException("The end date cannot be before the start date.", nameof(toDate));
        if (toDate >= clinicClock.Today)
            throw new ArgumentException("Historical analytics must end before today. Today is shown on the live board.", nameof(toDate));
    }

    private static ClinicTotals Totals(IReadOnlyList<DoctorAnalytics> rows)
    {
        var arrivals = rows.Sum(item => item.Metrics.ArrivalsCount);
        var served = rows.Sum(item => item.Metrics.ServedCount);
        var openHours = rows.Sum(item => item.Metrics.OpenHours);
        var serviceHours = rows.Sum(item => item.Metrics.MeasuredServiceHours ?? 0);
        var noShows = rows.Sum(item => item.Metrics.NoShowCount);
        var denominator = rows.Sum(item => item.Metrics.NoShowDenominator);
        var walkIns = rows.Sum(item => item.Metrics.WalkInCompletedCount);

        return new ClinicTotals(
            arrivals,
            served,
            openHours,
            openHours > 0 ? serviceHours / openHours : null,
            noShows,
            denominator,
            denominator > 0 ? (double)noShows / denominator : null,
            walkIns,
            openHours > 0 ? walkIns / openHours : null);
    }
}
