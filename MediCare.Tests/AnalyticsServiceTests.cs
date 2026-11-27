using MediCare.Application.Abstractions;
using MediCare.Application.Analytics;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Time;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

public sealed class AnalyticsServiceTests
{
    private static readonly TimeZoneInfo Cairo = ClinicTimeZoneResolver.Resolve("Africa/Cairo");

    // Tuesday 8 December 2026. Clinic-local date and time: 11:00 local is 09:00 UTC (UTC+2 in December).
    private static readonly DateOnly Today = new(2026, 12, 8);
    private static readonly DateTimeOffset NowUtc = new(2026, 12, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid DoctorOneId = Guid.NewGuid();
    private static readonly Guid DoctorTwoId = Guid.NewGuid();
    private static readonly Guid DoctorOneUserId = Guid.NewGuid();
    private static readonly Guid DoctorTwoUserId = Guid.NewGuid();

    [Fact]
    public async Task Doctor_statistics_are_resolved_from_the_signed_in_user_only()
    {
        var repository = CreateRepository();
        var service = CreateService(repository, NowUtc);

        var own = await service.GetOwnDoctorAnalyticsAsync(DoctorOneUserId, Today.AddDays(-7), Today.AddDays(-1), CancellationToken.None);

        Assert.Equal(DoctorOneId, own.DoctorId);
        Assert.Equal("Dr. One", own.DoctorName);
    }

    [Fact]
    public async Task A_user_without_a_doctor_profile_cannot_read_doctor_statistics()
    {
        var service = CreateService(CreateRepository(), NowUtc);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetOwnDoctorAnalyticsAsync(Guid.NewGuid(), Today.AddDays(-7), Today.AddDays(-1), CancellationToken.None));
    }

    [Fact]
    public async Task Historical_periods_must_end_before_today()
    {
        var service = CreateService(CreateRepository(), NowUtc);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetClinicAnalyticsAsync(Today.AddDays(-7), Today, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetDoctorAnalyticsAsync(DoctorOneId, Today.AddDays(-7), Today, CancellationToken.None));
    }

    [Fact]
    public async Task A_reversed_period_is_refused()
    {
        var service = CreateService(CreateRepository(), NowUtc);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetClinicAnalyticsAsync(Today.AddDays(-1), Today.AddDays(-7), CancellationToken.None));
    }

    [Fact]
    public async Task Historical_results_are_cached_and_live_results_are_not()
    {
        var repository = CreateRepository();
        var cache = new FakeCache();
        var service = CreateService(repository, NowUtc, cache);
        var from = Today.AddDays(-7);
        var to = Today.AddDays(-1);

        await service.GetDoctorAnalyticsAsync(DoctorOneId, from, to, CancellationToken.None);
        var afterFirst = repository.VisitQueries;
        await service.GetDoctorAnalyticsAsync(DoctorOneId, from, to, CancellationToken.None);
        Assert.Equal(afterFirst, repository.VisitQueries);

        await service.GetLiveAsync(null, CancellationToken.None);
        var afterLiveOne = repository.VisitQueries;
        await service.GetLiveAsync(null, CancellationToken.None);
        Assert.Equal(afterLiveOne + 1, repository.VisitQueries);
    }

    [Fact]
    public async Task Clinic_totals_pool_counts_and_weight_utilization_by_open_time()
    {
        var repository = CreateRepository();
        var service = CreateService(repository, NowUtc);

        var report = await service.GetClinicAnalyticsAsync(Today.AddDays(-1), Today.AddDays(-1), CancellationToken.None);

        // One historical day: doctor one has 2 arrivals and doctor two has 1; each has a 4-hour window (8 hours total).
        Assert.Equal(3, report.Totals.ArrivalsCount);
        Assert.Equal(8.0, report.Totals.OpenHours, 9);
        Assert.Equal(2, report.Rows.Count);
        // No appointments exist in this fixture, so the no-show rate is undefined rather than zero.
        Assert.Null(report.Totals.NoShowRate);
        Assert.Equal(1, report.Totals.WalkInCompletedCount);
    }

    [Fact]
    public async Task Live_board_describes_today_so_far_clipped_to_now()
    {
        var repository = CreateRepository();
        var service = CreateService(repository, NowUtc);

        var live = await service.GetLiveAsync(new[] { DoctorOneId }, CancellationToken.None);

        Assert.Equal(Today, live.Today);
        var row = Assert.Single(live.Rows);
        Assert.Equal(2, row.WaitingNow);
        Assert.Equal(1, row.InConsultation);
        Assert.Equal(0, row.CalledNow);
        // Waiting since 07:30 UTC is 90 minutes at 09:00 UTC.
        Assert.Equal(90.0, row.LongestCurrentWaitMinutes!.Value, 6);
        // Open 09:00-11:00 local (07:00-09:00 UTC), clipped to now: T = 2 hours.
        Assert.Equal(2.0, row.MetricsSoFar.OpenHours, 9);
        // Four attempts so far (two waiting, one in consultation, one completed): lambda = 4 / 2 = 2 per hour.
        Assert.Equal(2.0, row.MetricsSoFar.LambdaPerHour!.Value, 9);
        // One completed 20-minute consultation: mu = 3 per hour, rho = 2/3.
        Assert.Equal(3.0, row.MetricsSoFar.MuPerHour!.Value, 9);
        Assert.Equal(2.0 / 3.0, row.MetricsSoFar.Rho!.Value, 9);
        Assert.False(row.MetricsSoFar.Overloaded);
    }

    [Fact]
    public async Task Live_board_blends_the_consultation_length_with_the_last_30_days()
    {
        var repository = CreateRepository();
        // Five 40-minute consultations in the last 30 days; one 10-minute consultation 31 days ago is outside the window.
        for (var day = 1; day <= 5; day++)
            repository.ServiceHistory.Add(new AnalyticsServiceHistoryRow(DoctorOneId, Today.AddDays(-day), Utc(Today.AddDays(-day), 8, 0), Utc(Today.AddDays(-day), 8, 40)));
        repository.ServiceHistory.Add(new AnalyticsServiceHistoryRow(DoctorOneId, Today.AddDays(-31), Utc(Today.AddDays(-31), 8, 0), Utc(Today.AddDays(-31), 8, 10)));
        var service = CreateService(repository, NowUtc);

        var live = await service.GetLiveAsync(new[] { DoctorOneId }, CancellationToken.None);

        // Today has one 20-minute consultation: (20 + 5 * 40) / (1 + 5) = 220/6 min.
        var metrics = Assert.Single(live.Rows).MetricsSoFar;
        Assert.Equal(220.0 / 6.0, metrics.MeanServiceMinutes!.Value, 9);
        Assert.True(metrics.MeanServiceUsesHistory);
    }

    // ---------- Fixtures ----------

    private static AnalyticsService CreateService(FakeAnalyticsRepository repository, DateTimeOffset utcNow, FakeCache? cache = null) =>
        new(repository, new CairoClock(utcNow), Cairo, Options.Create(new ExpectedDurationOptions()), cache ?? new FakeCache());

    private static FakeAnalyticsRepository CreateRepository()
    {
        var repository = new FakeAnalyticsRepository();
        repository.Doctors.Add(new AnalyticsDoctor(DoctorOneId, DoctorOneUserId, "Dr. One", 20, true));
        repository.Doctors.Add(new AnalyticsDoctor(DoctorTwoId, DoctorTwoUserId, "Dr. Two", 20, true));

        var yesterday = Today.AddDays(-1);
        foreach (var doctorId in new[] { DoctorOneId, DoctorTwoId })
        {
            repository.WorkingHours.Add(new AnalyticsWorkingHourRow(doctorId, yesterday.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(13, 0)));
            repository.WorkingHours.Add(new AnalyticsWorkingHourRow(doctorId, Today.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(13, 0)));
        }

        // Yesterday: a completed visit for doctor one, a walk-in for doctor two.
        repository.Visits.Add(new AnalyticsVisitRow(DoctorOneId, Guid.NewGuid(), yesterday, QueueEntrySource.Scheduled, QueueEntryStatus.Completed,
            Utc(yesterday, 9, 0), null, Utc(yesterday, 9, 0), Utc(yesterday, 9, 20), false));
        repository.Visits.Add(new AnalyticsVisitRow(DoctorOneId, Guid.NewGuid(), yesterday, QueueEntrySource.WalkIn, QueueEntryStatus.Completed,
            Utc(yesterday, 9, 0), null, Utc(yesterday, 9, 20), Utc(yesterday, 9, 40), false));
        repository.Visits.Add(new AnalyticsVisitRow(DoctorTwoId, Guid.NewGuid(), yesterday, QueueEntrySource.Scheduled, QueueEntryStatus.Completed,
            Utc(yesterday, 9, 0), null, Utc(yesterday, 9, 0), Utc(yesterday, 9, 20), false));

        // Today (live), as seen at 09:00 UTC: two waiting since 07:30 and 08:00 UTC, one in consultation, one completed.
        var todayVisits = new[]
        {
            new AnalyticsVisitRow(DoctorOneId, Guid.NewGuid(), Today, QueueEntrySource.WalkIn, QueueEntryStatus.Waiting,
                new DateTimeOffset(2026, 12, 8, 7, 30, 0, TimeSpan.Zero), null, null, null, false),
            new AnalyticsVisitRow(DoctorOneId, Guid.NewGuid(), Today, QueueEntrySource.WalkIn, QueueEntryStatus.Waiting,
                new DateTimeOffset(2026, 12, 8, 8, 0, 0, TimeSpan.Zero), null, null, null, false),
            new AnalyticsVisitRow(DoctorOneId, Guid.NewGuid(), Today, QueueEntrySource.Scheduled, QueueEntryStatus.InConsultation,
                new DateTimeOffset(2026, 12, 8, 7, 40, 0, TimeSpan.Zero), null, new DateTimeOffset(2026, 12, 8, 7, 45, 0, TimeSpan.Zero), null, false),
            new AnalyticsVisitRow(DoctorOneId, Guid.NewGuid(), Today, QueueEntrySource.Scheduled, QueueEntryStatus.Completed,
                new DateTimeOffset(2026, 12, 8, 7, 0, 0, TimeSpan.Zero), null, new DateTimeOffset(2026, 12, 8, 7, 5, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 8, 7, 25, 0, TimeSpan.Zero), false)
        };
        repository.Visits.AddRange(todayVisits);
        return repository;
    }

    private static DateTimeOffset Utc(DateOnly day, int localHour, int localMinute) =>
        new CairoClock(NowUtc).ToUtc(day, new TimeOnly(localHour, localMinute));

    private sealed class CairoClock(DateTimeOffset utcNow) : IClinicClock
    {
        private readonly SystemClinicClock _inner = new(new FixedTime(utcNow), Cairo);

        public DateTimeOffset UtcNow { get; } = utcNow;
        public DateOnly Today => _inner.Today;
        public TimeOnly CurrentTime => _inner.CurrentTime;
        public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime) => _inner.ToUtc(clinicDate, clinicTime);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeCache : IAnalyticsResultCache
    {
        private readonly Dictionary<string, object> _items = [];

        public bool TryGet<T>(string key, out T? value) where T : class
        {
            if (_items.TryGetValue(key, out var stored) && stored is T typed)
            {
                value = typed;
                return true;
            }
            value = null;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan timeToLive) where T : class => _items[key] = value;
    }

    private sealed class FakeAnalyticsRepository : IAnalyticsRepository
    {
        public List<AnalyticsDoctor> Doctors { get; } = [];
        public List<AnalyticsVisitRow> Visits { get; } = [];
        public List<AnalyticsAppointmentRow> Appointments { get; } = [];
        public List<AnalyticsWorkingHourRow> WorkingHours { get; } = [];
        public List<AnalyticsHolidayRow> Holidays { get; } = [];
        public List<AnalyticsExtensionRow> Extensions { get; } = [];
        public List<AnalyticsBreakRow> Breaks { get; } = [];
        public int VisitQueries { get; private set; }

        private static bool InSet(Guid id, IReadOnlyCollection<Guid>? ids) => ids is null || ids.Contains(id);

        public Task<IReadOnlyList<AnalyticsDoctor>> GetDoctorsAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsDoctor>>(Doctors.Where(item => InSet(item.DoctorId, doctorIds)).ToArray());

        public Task<Guid?> GetDoctorIdForApplicationUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Doctors.SingleOrDefault(item => item.ApplicationUserId == applicationUserId)?.DoctorId);

        public Task<IReadOnlyList<AnalyticsVisitRow>> GetVisitsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
        {
            VisitQueries++;
            return Task.FromResult<IReadOnlyList<AnalyticsVisitRow>>(Visits
                .Where(item => InSet(item.DoctorId, doctorIds) && item.QueueDate >= fromDate && item.QueueDate <= toDate).ToArray());
        }

        public Task<IReadOnlyList<AnalyticsAppointmentRow>> GetAppointmentsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsAppointmentRow>>(Appointments.ToArray());

        public Task<IReadOnlyList<AnalyticsWorkingHourRow>> GetActiveWorkingHoursAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsWorkingHourRow>>(WorkingHours.Where(item => InSet(item.DoctorId, doctorIds)).ToArray());

        public Task<IReadOnlyList<AnalyticsHolidayRow>> GetHolidaysAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsHolidayRow>>(Holidays.Where(item => InSet(item.DoctorId, doctorIds)).ToArray());

        public Task<IReadOnlyList<AnalyticsExtensionRow>> GetApprovedExtensionsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsExtensionRow>>(Extensions.ToArray());

        public Task<IReadOnlyList<AnalyticsBreakRow>> GetBreaksAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsBreakRow>>(Breaks.ToArray());

        public List<AnalyticsServiceHistoryRow> ServiceHistory { get; } = [];

        public Task<IReadOnlyList<AnalyticsServiceHistoryRow>> GetServiceHistoryAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsServiceHistoryRow>>(ServiceHistory
                .Where(item => InSet(item.DoctorId, doctorIds) && item.QueueDate >= fromDate && item.QueueDate <= toDate).ToArray());
    }
}
