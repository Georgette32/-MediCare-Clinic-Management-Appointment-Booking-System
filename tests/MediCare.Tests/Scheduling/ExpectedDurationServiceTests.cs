using MediCare.Application.Common;
using MediCare.Application.DTOs.Analytics;
using MediCare.Application.DTOs.Scheduling;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Tests.Scheduling;

public sealed class ExpectedDurationServiceTests
{
    private static readonly DateOnly ClinicDate = new(2026, 12, 1);
    private const int DoctorConfiguredMinutes = 20;
    private static readonly DoctorProfile Doctor = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "LIC-" + Guid.NewGuid().ToString("N"),
        100m,
        5,
        null,
        DoctorConfiguredMinutes);
    private static readonly Guid DoctorId = Doctor.Id;

    // --- helpers ---

    private static ExpectedDurationService CreateService(
        IReadOnlyList<QueueEntry>? completedVisits = null,
        int? minimumSampleSize = null,
        int maxDurationMinutesCap = ExpectedDurationOptions.DefaultMaxDurationMinutesCap)
    {
        var appointmentRepo = new FakeAppointmentRepository(Doctor);
        var queueRepo = new FakeQueueRepository(completedVisits ?? []);
        var options = Options.Create(new ExpectedDurationOptions
        {
            MinimumSampleSize = minimumSampleSize,
            MaxDurationMinutesCap = maxDurationMinutesCap
        });

        return new ExpectedDurationService(appointmentRepo, queueRepo, options);
    }

    /// <summary>Builds a completed QueueEntry with the given actual duration in minutes.</summary>
    private static QueueEntry CompletedEntry(double durationMinutes)
    {
        var arrivedAt = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var calledAt = arrivedAt.AddMinutes(2);
        var startedAt = calledAt.AddMinutes(1);
        var completedAt = startedAt.AddMinutes(durationMinutes);

        var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), DoctorId, new DateOnly(2026, 11, 20), arrivedAt, 1);
        var approval = WalkInAdmissionApproval.Request(entry, arrivedAt.AddMinutes(-1), arrivedAt.AddHours(1));
        approval.Approve(arrivedAt);
        entry.CallWalkIn(approval, calledAt, entry.QueueDate);
        entry.StartConsultation(startedAt);
        entry.Complete(completedAt);
        return entry;
    }

    // --- tests ---

    [Fact]
    public async Task Returns_doctor_configured_duration_when_no_completed_visits()
    {
        var service = CreateService(completedVisits: [], minimumSampleSize: 3);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(DoctorConfiguredMinutes, result.DurationMinutes);
        Assert.False(result.UsedHistoricalAverage);
        Assert.Equal(0, result.ValidSampleCount);
        Assert.Null(result.RawAverageMinutes);
    }

    [Fact]
    public async Task Returns_doctor_configured_duration_when_sample_below_minimum()
    {
        // 2 visits but minimum is 3 -> fallback
        var visits = new[] { CompletedEntry(10), CompletedEntry(20) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 3);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(DoctorConfiguredMinutes, result.DurationMinutes);
        Assert.False(result.UsedHistoricalAverage);
        Assert.Equal(2, result.ValidSampleCount);
    }

    [Fact]
    public async Task Returns_rounded_average_when_sample_meets_minimum()
    {
        // Three 10-min visits -> average 10, with a threshold of 3
        var visits = new[] { CompletedEntry(10), CompletedEntry(10), CompletedEntry(10) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 3);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(10, result.DurationMinutes);
        Assert.True(result.UsedHistoricalAverage);
        Assert.Equal(3, result.ValidSampleCount);
    }

    [Fact]
    public async Task Falls_back_to_configured_duration_when_minimum_sample_size_is_unset()
    {
        var visits = new[] { CompletedEntry(10), CompletedEntry(10), CompletedEntry(10) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: null);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(DoctorConfiguredMinutes, result.DurationMinutes);
        Assert.False(result.UsedHistoricalAverage);
        Assert.Equal(3, result.ValidSampleCount);
        Assert.Equal(10, result.RawAverageMinutes);
    }

    [Fact]
    public async Task Returns_rounded_average_when_sample_exactly_meets_minimum_sample_size()
    {
        // 3 visits of 15 min each, minimum = 3
        var visits = new[] { CompletedEntry(15), CompletedEntry(15), CompletedEntry(15) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 3);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(15, result.DurationMinutes);
        Assert.True(result.UsedHistoricalAverage);
    }

    [Fact]
    public async Task Rounds_average_half_up()
    {
        // 5 and 6 -> average 5.5 -> rounds up to 6
        var visits = new[] { CompletedEntry(5), CompletedEntry(6) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 2);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(6, result.DurationMinutes);
        Assert.True(result.UsedHistoricalAverage);
    }

    [Fact]
    public async Task Filters_out_visits_at_or_above_cap()
    {
        // cap = 60; a 60-min visit should be excluded; 10 and 20 included -> avg 15
        var visits = new[] { CompletedEntry(10), CompletedEntry(20), CompletedEntry(60) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 2, maxDurationMinutesCap: 60);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(15, result.DurationMinutes);
        Assert.Equal(2, result.ValidSampleCount);
    }

    [Fact]
    public async Task Falls_back_when_all_visits_exceed_cap()
    {
        // All visits are at or above the cap -> no valid samples -> fallback
        var visits = new[] { CompletedEntry(60), CompletedEntry(90) };
        var service = CreateService(completedVisits: visits, maxDurationMinutesCap: 60);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(DoctorConfiguredMinutes, result.DurationMinutes);
        Assert.False(result.UsedHistoricalAverage);
        Assert.Equal(0, result.ValidSampleCount);
    }

    [Fact]
    public async Task Throws_when_doctor_not_found()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CalculateExpectedDurationMinutesAsync(Guid.NewGuid(), ClinicDate, CancellationToken.None));
    }

    // --- fakes ---

    private sealed class FakeAppointmentRepository(DoctorProfile doctor) : IAppointmentRepository
    {
        public Task<DoctorProfile?> GetDoctorProfileAsync(Guid doctorProfileId, CancellationToken ct) =>
            Task.FromResult(doctorProfileId == doctor.Id ? (DoctorProfile?)doctor : null);

        public Task<DoctorProfile?> GetDoctorProfileByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(userId == doctor.UserId ? (DoctorProfile?)doctor : null);

        public Task<bool> PatientExistsAsync(Guid patientProfileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientProfileId, Guid doctorProfileId, DateOnly date, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorProfileId, DayOfWeek dayOfWeek, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> IsHolidayAsync(Guid doctorProfileId, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasBreakOverlapAsync(Guid doctorProfileId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasAppointmentConflictAsync(Guid doctorProfileId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Appointment appointment, CancellationToken ct) => throw new NotImplementedException();
        public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorProfileId, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAsync(Guid doctorProfileId, CancellationToken ct) => throw new NotImplementedException();

        public Task<int> CountAsync(AppointmentStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountUpcomingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountByDateRangeAsync(DateTime startDateUtc, DateTime endDateUtc, AppointmentStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<PeakHoursDto>> GetPeakHoursAsync(DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PeakHoursDto>>([]);
        public Task<IReadOnlyList<SpecializationAnalyticsDto>> GetPopularSpecializationsAsync(DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SpecializationAnalyticsDto>>([]);
        public Task<IReadOnlyList<PendingAppointmentDto>> GetPendingWithNamesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PendingAppointmentDto>>([]);
        public Task<string?> GetPatientNameAsync(Guid patientProfileId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeQueueRepository(IReadOnlyList<QueueEntry> completedVisits) : IQueueRepository
    {
        public Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorProfileId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
            Task.FromResult(completedVisits);

        public Task<bool> HasQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasActiveEntryForPatientAsync(Guid patientProfileId, Guid doctorProfileId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<long> GetNextWalkInSequenceAsync(Guid doctorProfileId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(QueueEntry queueEntry, CancellationToken ct) => throw new NotImplementedException();
        public Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorProfileId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorProfileId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorProfileId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
    }
}
