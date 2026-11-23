using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class CheckInServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 15, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 12, 1);

    [Fact]
    public async Task Scheduled_check_in_creates_scheduled_queue_entry_with_null_sequence()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        var result = await fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None);

        Assert.False(result.RequiresWalkInRegistration);
        var entry = fixture.QueueRepository.Items[result.QueueEntryId!.Value];
        Assert.Equal(QueueEntrySource.Scheduled, entry.Source);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
        Assert.Equal(appointment.Id, entry.AppointmentId);
        Assert.Equal(appointment.PatientId, entry.PatientId);
        Assert.Equal(appointment.DoctorId, entry.DoctorId);
        Assert.Equal(Today, entry.QueueDate);
        Assert.Equal(Now, entry.ArrivedAt);
        Assert.Null(entry.QueueSequence);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Scheduled_check_in_fails_if_appointment_does_not_exist()
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CheckInScheduledPatientAsync(Guid.NewGuid(), fixture.DoctorId, CancellationToken.None));

        Assert.Equal("The appointment was not found.", exception.Message);
    }

    [Fact]
    public async Task Scheduled_check_in_fails_if_appointment_is_pending()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddPendingAppointment(Today);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None));

        Assert.Equal("Only confirmed appointments can check in.", exception.Message);
        Assert.Empty(fixture.QueueRepository.Items);
    }

    [Fact]
    public async Task Scheduled_check_in_fails_if_appointment_is_rejected_or_cancelled()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);
        appointment.CancelByClinic("Doctor ill", Now);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None));

        Assert.Equal("Only confirmed appointments can check in.", exception.Message);
    }

    [Fact]
    public async Task Scheduled_check_in_fails_if_appointment_date_does_not_match_today()
    {
        var fixture = CreateFixture();
        var futureAppointment = fixture.AddConfirmedAppointment(Today.AddDays(1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CheckInScheduledPatientAsync(futureAppointment.Id, fixture.DoctorId, CancellationToken.None));

        Assert.Equal("Check-in is permitted only on the day of the appointment.", exception.Message);
    }

    [Fact]
    public async Task Scheduled_check_in_fails_on_duplicate_check_in_attempt()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        await fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None));

        Assert.Equal("The patient has already checked in for this appointment.", exception.Message);
        Assert.Single(fixture.QueueRepository.Items);
    }

    [Fact]
    public async Task Scheduled_check_in_rejects_wrong_expected_doctor()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CheckInScheduledPatientAsync(appointment.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("The appointment does not belong to the selected doctor.", exception.Message);
        Assert.Empty(fixture.QueueRepository.Items);
    }

    [Fact]
    public async Task Scheduled_check_in_within_grace_period_keeps_scheduled_priority()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);

        var result = await fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None);

        Assert.False(result.RequiresWalkInRegistration);
        Assert.NotNull(result.QueueEntryId);
        Assert.Equal(QueueEntrySource.Scheduled, fixture.QueueRepository.Items[result.QueueEntryId!.Value].Source);
        Assert.Equal(60, appointment.FrozenGracePeriodMinutes);
        Assert.Equal(fixture.Clock.ToUtc(Today, new TimeOnly(9, 0)), appointment.GracePeriodStartedAtUtc);
    }

    [Fact]
    public async Task Scheduled_check_in_at_exact_grace_expiry_marks_no_show_and_guides_walk_in()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);
        fixture.Clock.UtcNow = fixture.Clock.ToUtc(Today, new TimeOnly(10, 0));

        var result = await fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None);

        Assert.True(result.RequiresWalkInRegistration);
        Assert.Null(result.QueueEntryId);
        Assert.Contains("No-Show", result.Message);
        Assert.Contains("walk-in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Empty(fixture.QueueRepository.Items);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Scheduled_check_in_after_grace_marks_confirmed_appointment_no_show_and_guides_walk_in()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);
        fixture.Clock.UtcNow = fixture.Clock.ToUtc(Today, new TimeOnly(10, 1));

        var result = await fixture.Service.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None);

        Assert.True(result.RequiresWalkInRegistration);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Empty(fixture.QueueRepository.Items);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Walk_in_registration_assigns_first_sequence_1_for_doctor_and_day()
    {
        var fixture = CreateFixture();

        var queueEntryId = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);

        var entry = fixture.QueueRepository.Items[queueEntryId];
        Assert.Equal(QueueEntrySource.WalkIn, entry.Source);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
        Assert.Null(entry.AppointmentId);
        Assert.Equal(fixture.PatientId, entry.PatientId);
        Assert.Equal(fixture.DoctorId, entry.DoctorId);
        Assert.Equal(Today, entry.QueueDate);
        Assert.Equal(1L, entry.QueueSequence);
        Assert.Equal(Now, entry.ArrivedAt);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Walk_in_registration_increments_sequence_for_same_doctor_and_day()
    {
        var fixture = CreateFixture();
        var patient2 = fixture.AddPatient();
        var patient3 = fixture.AddPatient();

        var entry1Id = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);
        var entry2Id = await fixture.Service.RegisterWalkInAsync(patient2, fixture.DoctorId, CancellationToken.None);
        var entry3Id = await fixture.Service.RegisterWalkInAsync(patient3, fixture.DoctorId, CancellationToken.None);

        Assert.Equal(1L, fixture.QueueRepository.Items[entry1Id].QueueSequence);
        Assert.Equal(2L, fixture.QueueRepository.Items[entry2Id].QueueSequence);
        Assert.Equal(3L, fixture.QueueRepository.Items[entry3Id].QueueSequence);
    }

    [Fact]
    public async Task Walk_in_registration_uses_independent_sequences_for_different_doctors()
    {
        var fixture = CreateFixture();
        var doctor2 = fixture.AddDoctor("Dr. Adams");
        var patient2 = fixture.AddPatient();

        var entryDoc1Id = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);
        var entryDoc2Id = await fixture.Service.RegisterWalkInAsync(patient2, doctor2, CancellationToken.None);

        Assert.Equal(1L, fixture.QueueRepository.Items[entryDoc1Id].QueueSequence);
        Assert.Equal(1L, fixture.QueueRepository.Items[entryDoc2Id].QueueSequence);
    }

    [Fact]
    public async Task Walk_in_registration_uses_independent_sequences_for_different_dates()
    {
        var fixture = CreateFixture();
        var entryDay1Id = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);

        fixture.Clock.Today = Today.AddDays(1);
        var entryDay2Id = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);

        Assert.Equal(1L, fixture.QueueRepository.Items[entryDay1Id].QueueSequence);
        Assert.Equal(1L, fixture.QueueRepository.Items[entryDay2Id].QueueSequence);
    }

    [Fact]
    public async Task Walk_in_registration_fails_if_patient_does_not_exist()
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterWalkInAsync(Guid.NewGuid(), fixture.DoctorId, CancellationToken.None));

        Assert.Equal("The patient was not found.", exception.Message);
    }

    [Fact]
    public async Task Walk_in_registration_fails_if_doctor_is_not_found_or_inactive()
    {
        var fixture = CreateFixture();
        var inactiveDoctor = fixture.AddDoctor("Dr. Inactive", isActive: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterWalkInAsync(fixture.PatientId, inactiveDoctor, CancellationToken.None));

        Assert.Equal("The active doctor was not found.", exception.Message);
    }

    [Fact]
    public async Task Walk_in_registration_fails_if_patient_already_active_in_queue_today()
    {
        var fixture = CreateFixture();

        await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None));

        Assert.Equal("The patient is already in the queue for this doctor today.", exception.Message);
    }

    [Fact]
    public async Task Walk_in_registration_is_blocked_by_confirmed_appointment()
    {
        var fixture = CreateFixture();
        fixture.AddConfirmedAppointment(Today);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None));

        Assert.Equal("The patient has a confirmed appointment with this doctor today. Use scheduled check-in.", exception.Message);
        Assert.Empty(fixture.QueueRepository.Items);
    }

    [Fact]
    public async Task Walk_in_registration_is_allowed_after_appointment_becomes_no_show()
    {
        var fixture = CreateFixture();
        var appointment = fixture.AddConfirmedAppointment(Today);
        appointment.MarkNoShow(Now);

        var id = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);

        Assert.Equal(QueueEntrySource.WalkIn, fixture.QueueRepository.Items[id].Source);
    }

    [Fact]
    public async Task Walk_in_registration_is_allowed_after_terminal_queue_entries()
    {
        foreach (var terminalStatus in new[] { QueueEntryStatus.Cancelled, QueueEntryStatus.NoShow, QueueEntryStatus.Completed })
        {
            var fixture = CreateFixture();
            var firstId = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);
            var first = fixture.QueueRepository.Items[firstId];
            switch (terminalStatus)
            {
                case QueueEntryStatus.Cancelled: first.Cancel(); break;
                case QueueEntryStatus.NoShow: first.MarkNoShow(); break;
                case QueueEntryStatus.Completed:
                    var approval = WalkInAdmissionApproval.Request(first, Now.AddMinutes(-1), Now.AddHours(1));
                    approval.Approve(Now);
                    first.CallWalkIn(approval, Now, Today);
                    first.StartConsultation(Now.AddMinutes(1));
                    first.Complete(Now.AddMinutes(2));
                    break;
            }

            var secondId = await fixture.Service.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);
            Assert.Equal(2L, fixture.QueueRepository.Items[secondId].QueueSequence);
        }
    }

    private static Fixture CreateFixture()
    {
        var appointmentRepo = new FakeAppointmentRepository();
        var queueRepo = new FakeQueueRepository();
        var unitOfWork = new FakeUnitOfWork();
        var clock = new FakeClinicClock(Now, Today);

        var patientId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        appointmentRepo.PatientIds.Add(patientId);
        appointmentRepo.Doctors.Add(doctorId, new Doctor(doctorId, Guid.NewGuid(), "Dr. Smith", Guid.NewGuid(), 20));

        var expectedDuration = new FakeExpectedDurationService();
        var service = new CheckInService(appointmentRepo, queueRepo, expectedDuration, unitOfWork, clock);
        return new Fixture(service, appointmentRepo, queueRepo, unitOfWork, clock, patientId, doctorId);
    }

    private sealed record Fixture(
        CheckInService Service,
        FakeAppointmentRepository AppointmentRepository,
        FakeQueueRepository QueueRepository,
        FakeUnitOfWork UnitOfWork,
        FakeClinicClock Clock,
        Guid PatientId,
        Guid DoctorId)
    {
        public Appointment AddConfirmedAppointment(DateOnly date)
        {
            var appointment = Appointment.Request(PatientId, DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20), Clock.UtcNow);
            appointment.Confirm(Clock.UtcNow);
            AppointmentRepository.Items.Add(appointment.Id, appointment);
            return appointment;
        }

        public Appointment AddPendingAppointment(DateOnly date)
        {
            var appointment = Appointment.Request(PatientId, DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20), Clock.UtcNow);
            AppointmentRepository.Items.Add(appointment.Id, appointment);
            return appointment;
        }

        public Guid AddPatient()
        {
            var id = Guid.NewGuid();
            AppointmentRepository.PatientIds.Add(id);
            return id;
        }

        public Guid AddDoctor(string name, bool isActive = true)
        {
            var id = Guid.NewGuid();
            AppointmentRepository.Doctors.Add(id, new Doctor(id, Guid.NewGuid(), name, Guid.NewGuid(), 20, isActive));
            return id;
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

    private sealed class FakeExpectedDurationService : IExpectedDurationService
    {
        public Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
            Task.FromResult(new ExpectedDurationResult(20, false, 0, null));
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
        public HashSet<Guid> PatientIds { get; } = [];
        public Dictionary<Guid, Doctor> Doctors { get; } = [];
        public Dictionary<Guid, Appointment> Items { get; } = [];

        public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult(PatientIds.Contains(patientId));

        public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Doctors.Values.SingleOrDefault(doctor => doctor.ApplicationUserId == applicationUserId));

        public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Any(appointment => appointment.PatientId == patientId && appointment.DoctorId == doctorId &&
                appointment.AppointmentDate == date && appointment.Status == AppointmentStatus.Confirmed));

        public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken)
        {
            var doc = Doctors.GetValueOrDefault(doctorId);
            return Task.FromResult(doc is { IsActive: true } ? doc : null);
        }

        public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(appointmentId));

        public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task AddAsync(Appointment appointment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Appointment>>(Items.Values
                .Where(a => a.DoctorId == doctorId && a.AppointmentDate == date && a.Status == AppointmentStatus.Confirmed)
                .ToArray());
    }

    private sealed class FakeQueueRepository : IQueueRepository
    {
        public Dictionary<Guid, QueueEntry> Items { get; } = [];

        public Task<bool> HasQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Any(item => item.AppointmentId == appointmentId));

        public Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Any(item =>
                item.PatientId == patientId &&
                item.DoctorId == doctorId &&
                item.QueueDate == queueDate &&
                item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation));

        public Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken)
        {
            var max = Items.Values
                .Where(item => item.DoctorId == doctorId && item.QueueDate == queueDate && item.QueueSequence != null)
                .Select(item => item.QueueSequence)
                .DefaultIfEmpty(0L)
                .Max();

            return Task.FromResult((max ?? 0L) + 1L);
        }

        public Task AddAsync(QueueEntry queueEntry, CancellationToken cancellationToken)
        {
            Items.Add(queueEntry.Id, queueEntry);
            return Task.CompletedTask;
        }

        public Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));

        public Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.SingleOrDefault(item => item.AppointmentId == appointmentId));

        public Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueueEntry>>(Items.Values
                .Where(item => item.DoctorId == doctorId &&
                               item.QueueDate == queueDate &&
                               item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
                .OrderBy(item => item.ArrivedAt)
                .ThenBy(item => item.QueueSequence)
                .ToArray());

        public Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueueEntry>>(Items.Values
                .Where(item => item.DoctorId == doctorId && item.QueueDate == queueDate && item.Source == QueueEntrySource.Scheduled &&
                    item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
                .OrderBy(item => item.Appointment?.StartTime)
                .ThenBy(item => item.ArrivedAt)
                .ToArray());

        public Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueueEntry>>(Items.Values
                .Where(item => item.DoctorId == doctorId && item.QueueDate == queueDate && item.Source == QueueEntrySource.WalkIn &&
                    item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
                .OrderBy(item => item.ArrivedAt)
                .ThenBy(item => item.QueueSequence)
                .ToArray());

        public Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueueEntry>>(Items.Values
                .Where(item => item.DoctorId == doctorId &&
                               item.QueueDate >= fromDate &&
                               item.QueueDate <= toDate &&
                               item.Status == QueueEntryStatus.Completed &&
                               item.ConsultationStartedAt.HasValue &&
                               item.CompletedAt.HasValue)
                .ToArray());
    }
}
