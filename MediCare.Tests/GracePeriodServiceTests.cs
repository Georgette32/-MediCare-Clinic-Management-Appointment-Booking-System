using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

public sealed class GracePeriodServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 12, 1);
    private const int DoctorConfiguredMinutes = 20;
    // GracePeriodMultiplier = 3  →  expected grace period = 20 * 3 = 60 min

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static Fixture CreateFixture(DateTimeOffset? clock = null)
    {
        var now = clock ?? Now;
        var appointmentRepo = new FakeAppointmentRepository();
        var queueRepo = new FakeQueueRepository();
        var unitOfWork = new FakeUnitOfWork();
        var clinicClock = new FakeClinicClock(now, Today);

        var options = Options.Create(new ExpectedDurationOptions
        {
            MinimumSampleSize = null,            // no minimum → always use average if data exists
            MaxDurationMinutesCap = ExpectedDurationOptions.DefaultMaxDurationMinutesCap
        });
        var expectedDurationService = new ExpectedDurationService(appointmentRepo, queueRepo, options);
        var service = new GracePeriodService(appointmentRepo, queueRepo, expectedDurationService, unitOfWork, clinicClock);

        var doctorId = Guid.NewGuid();
        appointmentRepo.Doctors[doctorId] = new Doctor(doctorId, Guid.NewGuid(), "Dr. Test", Guid.NewGuid(), DoctorConfiguredMinutes);

        return new Fixture(service, appointmentRepo, queueRepo, unitOfWork, clinicClock, doctorId);
    }

    // ─── StartGracePeriodIfEligibleAsync tests ──────────────────────────────────

    [Fact]
    public async Task Start_freezes_grace_period_minutes_on_eligible_confirmed_appointment()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        var result = await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);

        Assert.True(result.StartedSuccessfully);
        Assert.Equal(appointment.Id, result.AppointmentId);
        // No historical data → doctor configured minutes × multiplier
        Assert.Equal(DoctorConfiguredMinutes * ExpectedDurationOptions.GracePeriodMultiplier, result.FrozenGracePeriodMinutes);
        Assert.Equal(Now, result.StartedAtUtc);
        Assert.Equal(DoctorConfiguredMinutes * ExpectedDurationOptions.GracePeriodMultiplier, appointment.FrozenGracePeriodMinutes);
        Assert.Equal(Now.ToUniversalTime(), appointment.GracePeriodStartedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Start_returns_not_started_when_grace_period_already_frozen()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        // First call succeeds
        await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);

        // Second call: already frozen → returns StartedSuccessfully:false
        var result = await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);

        Assert.False(result.StartedSuccessfully);
        Assert.Equal(appointment.Id, result.AppointmentId);
        Assert.Equal(DoctorConfiguredMinutes * ExpectedDurationOptions.GracePeriodMultiplier, result.FrozenGracePeriodMinutes);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount); // only committed once
    }

    [Fact]
    public async Task Start_returns_not_started_before_scheduled_start_time()
    {
        var fixture = CreateFixture();
        var appointment = Appointment.Request(Guid.NewGuid(), fixture.DoctorId, Today, new TimeOnly(10, 0), new TimeOnly(10, 20), Now);
        appointment.Confirm(Now);
        fixture.AppointmentRepo.Items[appointment.Id] = appointment;

        var result = await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);

        Assert.False(result.StartedSuccessfully);
        Assert.Equal(0, result.FrozenGracePeriodMinutes);
        Assert.Null(appointment.FrozenGracePeriodMinutes);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Start_succeeds_exactly_at_scheduled_start_time()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        var result = await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);

        Assert.True(result.StartedSuccessfully);
        Assert.Equal(DoctorConfiguredMinutes * ExpectedDurationOptions.GracePeriodMultiplier, result.FrozenGracePeriodMinutes);
        Assert.Equal(Now, result.StartedAtUtc);
    }

    [Fact]
    public async Task Start_run_ten_minutes_late_still_anchors_grace_to_scheduled_start()
    {
        var fixture = CreateFixture(Now.AddMinutes(10));
        var appointment = fixture.AddConfirmedAppointment(Today);

        var result = await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);
        var scheduledStartUtc = fixture.Clock.ToUtc(Today, new TimeOnly(9, 0));

        Assert.True(result.StartedSuccessfully);
        Assert.Equal(scheduledStartUtc, result.StartedAtUtc);
        Assert.Equal(scheduledStartUtc, appointment.GracePeriodStartedAtUtc);
        Assert.Equal(DoctorConfiguredMinutes * ExpectedDurationOptions.GracePeriodMultiplier, appointment.FrozenGracePeriodMinutes);
        Assert.False(appointment.IsGracePeriodExpired(scheduledStartUtc.AddMinutes(59)));
        Assert.True(appointment.IsGracePeriodExpired(scheduledStartUtc.AddMinutes(60)));
    }

    [Fact]
    public async Task Process_marks_no_show_when_late_run_occurs_after_scheduled_start_plus_grace()
    {
        var fixture = CreateFixture(Now.AddMinutes(70));
        var appointment = fixture.AddConfirmedAppointment(Today);

        var startResult = await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);
        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(fixture.Clock.ToUtc(Today, new TimeOnly(9, 0)), startResult.StartedAtUtc);
        Assert.Equal(1, marked);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
    }

    [Fact]
    public async Task Process_marks_no_show_at_the_exact_expiry_boundary()
    {
        var fixture = CreateFixture(Now.AddMinutes(60));
        var appointment = fixture.AddConfirmedAppointment(Today);

        await fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);
        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(1, marked);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
    }

    [Fact]
    public async Task Start_throws_when_appointment_not_found()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StartGracePeriodIfEligibleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Start_throws_when_appointment_is_not_confirmed()
    {
        var fixture = CreateFixture();
        // Pending appointment
        var appointment = Appointment.Request(Guid.NewGuid(), fixture.DoctorId, Today, new TimeOnly(9, 0), new TimeOnly(9, 20), Now);
        fixture.AppointmentRepo.Items[appointment.Id] = appointment;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Start_throws_when_appointment_date_does_not_match_today()
    {
        var fixture = CreateFixture();
        // Tomorrow's appointment
        var appointment = fixture.AddConfirmedAppointment(Today.AddDays(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Start_throws_when_patient_already_checked_in()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        // Simulate an existing queue entry for this appointment
        var entry = QueueEntry.CreateScheduled(appointment.PatientId, appointment.DoctorId, appointment.Id, Today, Now.AddMinutes(-5));
        fixture.QueueRepo.Items[entry.Id] = entry;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None));
    }

    // ─── ProcessExpiredGracePeriodsForDoctorAsync tests ───────────────────────

    [Fact]
    public async Task Process_marks_appointment_no_show_when_grace_period_expired()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        // Freeze the grace period 61 minutes ago with a 60-min window
        var startedAt = Now.AddMinutes(-61);
        appointment.StartGracePeriod(60, startedAt);

        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(1, marked);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Process_rejects_non_current_clinic_date()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);
        appointment.StartGracePeriod(60, Now.AddMinutes(-61));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today.AddDays(-1), CancellationToken.None));

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Process_does_not_mark_no_show_when_grace_period_not_yet_expired()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        // Started 10 minutes ago with a 60-min window → still has 50 minutes left
        appointment.StartGracePeriod(60, Now.AddMinutes(-10));

        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(0, marked);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Process_skips_appointments_without_frozen_grace_period()
    {
        var fixture = CreateFixture();
        // Confirmed appointment with no grace period started
        fixture.AddConfirmedAppointment(Today);

        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(0, marked);
    }

    [Fact]
    public async Task Process_skips_appointments_where_patient_already_checked_in()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        // Grace period expired
        appointment.StartGracePeriod(60, Now.AddMinutes(-61));

        // But patient checked in
        var entry = QueueEntry.CreateScheduled(appointment.PatientId, appointment.DoctorId, appointment.Id, Today, Now.AddMinutes(-5));
        fixture.QueueRepo.Items[entry.Id] = entry;

        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(0, marked);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
    }

    [Fact]
    public async Task Process_marks_multiple_expired_appointments_in_one_pass()
    {
        var fixture = CreateFixture();

        var appt1 = fixture.AddConfirmedAppointment(Today);
        var appt2 = fixture.AddConfirmedAppointment(Today);
        var appt3 = fixture.AddConfirmedAppointment(Today); // not yet expired

        appt1.StartGracePeriod(60, Now.AddMinutes(-61));
        appt2.StartGracePeriod(60, Now.AddMinutes(-61));
        appt3.StartGracePeriod(60, Now.AddMinutes(-10));

        var marked = await fixture.Service.ProcessExpiredGracePeriodsForDoctorAsync(fixture.DoctorId, Today, CancellationToken.None);

        Assert.Equal(2, marked);
        Assert.Equal(AppointmentStatus.NoShow, appt1.Status);
        Assert.Equal(AppointmentStatus.NoShow, appt2.Status);
        Assert.Equal(AppointmentStatus.Confirmed, appt3.Status);
    }

    // ─── inner types ───────────────────────────────────────────────────────────

    private sealed record Fixture(
        GracePeriodService Service,
        FakeAppointmentRepository AppointmentRepo,
        FakeQueueRepository QueueRepo,
        FakeUnitOfWork UnitOfWork,
        FakeClinicClock Clock,
        Guid DoctorId)
    {
        public Appointment AddConfirmedAppointment(DateOnly date)
        {
            var appointment = Appointment.Request(Guid.NewGuid(), DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20), Clock.UtcNow);
            appointment.Confirm(Clock.UtcNow);
            AppointmentRepo.Items[appointment.Id] = appointment;
            return appointment;
        }
    }

    private sealed class FakeClinicClock(DateTimeOffset utcNow, DateOnly today) : IClinicClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public DateOnly Today { get; set; } = today;
        public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
        public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
            new(DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
            Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(this));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            await using var transaction = await BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await operation(cancellationToken);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            ExecuteSerializableWithRetryAsync(operation, cancellationToken);

        public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            ExecuteSerializableWithRetryAsync(operation, cancellationToken);

        private sealed class FakeTransaction(FakeUnitOfWork owner) : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                owner.CommitCount++;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        public Dictionary<Guid, Doctor> Doctors { get; } = [];
        public Dictionary<Guid, Appointment> Items { get; } = [];

        public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken ct) =>
            Task.FromResult(Doctors.GetValueOrDefault(doctorId) is { IsActive: true } d ? (Doctor?)d : null);

        public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken ct) =>
            Task.FromResult(Doctors.Values.SingleOrDefault(doctor => doctor.ApplicationUserId == applicationUserId));

        public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken ct) =>
            Task.FromResult(Items.Values.Any(item => item.PatientId == patientId && item.DoctorId == doctorId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed));

        public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken ct) =>
            Task.FromResult(Items.GetValueOrDefault(appointmentId));

        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Appointment>>(Items.Values
                .Where(a => a.DoctorId == doctorId && a.AppointmentDate == date && a.Status == AppointmentStatus.Confirmed)
                .ToArray());

        public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptId, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Appointment appointment, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeQueueRepository : IQueueRepository
    {
        public Dictionary<Guid, QueueEntry> Items { get; } = [];

        public Task<bool> HasQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken ct) =>
            Task.FromResult(Items.Values.Any(item => item.AppointmentId == appointmentId));

        public Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<QueueEntry>>(Items.Values
                .Where(item => item.DoctorId == doctorId &&
                               item.QueueDate >= fromDate &&
                               item.QueueDate <= toDate &&
                               item.Status == QueueEntryStatus.Completed &&
                               item.ConsultationStartedAt.HasValue &&
                               item.CompletedAt.HasValue)
                .ToArray());

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
