using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

public sealed class ExpectedDurationServiceTests
{
    private static readonly DateOnly ClinicDate = new(2026, 12, 1);
    private static readonly Guid DoctorId = Guid.NewGuid();
    private const int DoctorConfiguredMinutes = 20;

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static ExpectedDurationService CreateService(
        IReadOnlyList<QueueEntry>? completedVisits = null,
        int? minimumSampleSize = null,
        int maxDurationMinutesCap = ExpectedDurationOptions.DefaultMaxDurationMinutesCap)
    {
        var appointmentRepo = new FakeAppointmentRepository(DoctorId, DoctorConfiguredMinutes);
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
        var arrivedAt = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
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

    // ─── tests ─────────────────────────────────────────────────────────────────

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
        // 2 visits but minimum is 3 → fallback
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
        // Three 10-min visits → average 10, with a threshold of 3
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
        // 5 and 6 → average 5.5 → rounds up to 6
        var visits = new[] { CompletedEntry(5), CompletedEntry(6) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 2);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(6, result.DurationMinutes);
        Assert.True(result.UsedHistoricalAverage);
    }

    [Fact]
    public async Task Filters_out_visits_at_or_above_cap()
    {
        // cap = 60; a 60-min visit should be excluded; 10 and 20 included → avg 15
        var visits = new[] { CompletedEntry(10), CompletedEntry(20), CompletedEntry(60) };
        var service = CreateService(completedVisits: visits, minimumSampleSize: 2, maxDurationMinutesCap: 60);

        var result = await service.CalculateExpectedDurationMinutesAsync(DoctorId, ClinicDate, CancellationToken.None);

        Assert.Equal(15, result.DurationMinutes);
        Assert.Equal(2, result.ValidSampleCount);
    }

    [Fact]
    public async Task Falls_back_when_all_visits_exceed_cap()
    {
        // All visits are at or above the cap → no valid samples → fallback
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

    // ─── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeAppointmentRepository(Guid doctorId, int appointmentDurationMinutes) : IAppointmentRepository
    {
        private readonly Doctor _doctor = new(doctorId, Guid.NewGuid(), "Dr. Test", Guid.NewGuid(), appointmentDurationMinutes);

        public Task<Doctor?> GetDoctorAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(id == doctorId ? (Doctor?)_doctor : null);

        public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken ct) =>
            Task.FromResult(applicationUserId == _doctor.ApplicationUserId ? _doctor : null);

        public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptId, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Appointment appointment, CancellationToken ct) => throw new NotImplementedException();
        public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeQueueRepository(IReadOnlyList<QueueEntry> completedVisits) : IQueueRepository
    {
        public Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
            Task.FromResult(completedVisits);

        public Task<bool> HasQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(QueueEntry queueEntry, CancellationToken ct) => throw new NotImplementedException();
        public Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken ct) => throw new NotImplementedException();
    }
}
