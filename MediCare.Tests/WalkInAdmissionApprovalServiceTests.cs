using System.Data;
using System.ComponentModel.DataAnnotations;
using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

public sealed class WalkInAdmissionApprovalServiceTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly DateTimeOffset Start = new(2026, 12, 1, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task Request_is_per_queue_entry_duplicate_pending_is_blocked_and_approval_does_not_call()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();

        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);

        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
        var approval = fixture.Approvals.Items[approvalId];
        Assert.Equal(entry.Id, approval.QueueEntryId);
        Assert.Equal(fixture.DoctorId, approval.DoctorId);
        Assert.Equal(WalkInAdmissionDecision.Pending, approval.Decision);
        Assert.Equal(Start.AddMinutes(30), approval.ExpiresAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RequestAsync(entry.Id, CancellationToken.None));

        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, "approved", CancellationToken.None);
        Assert.Equal(WalkInAdmissionDecision.Approved, approval.Decision);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task Rejection_preserves_fifo_position_and_allows_a_new_request()
    {
        var fixture = CreateFixture();
        var earlier = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-5), sequence: 1);
        var later = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-4), sequence: 2);
        var before = await fixture.Queue.GetWalkInEntriesForDoctorAndDateAsync(fixture.DoctorId, Today, CancellationToken.None);
        var firstApprovalId = await fixture.Service.RequestAsync(earlier.Id, CancellationToken.None);

        await fixture.Service.DecideAsync(firstApprovalId, fixture.DoctorUserId, approve: false, "not yet", CancellationToken.None);
        var after = await fixture.Queue.GetWalkInEntriesForDoctorAndDateAsync(fixture.DoctorId, Today, CancellationToken.None);
        var secondApprovalId = await fixture.Service.RequestAsync(earlier.Id, CancellationToken.None);

        Assert.Equal(before.Select(entry => entry.Id), after.Select(entry => entry.Id));
        Assert.Equal(QueueEntryStatus.Waiting, earlier.Status);
        Assert.NotEqual(firstApprovalId, secondApprovalId);
        Assert.Equal(WalkInAdmissionDecision.Rejected, fixture.Approvals.Items[firstApprovalId].Decision);
        Assert.Equal(WalkInAdmissionDecision.Pending, fixture.Approvals.Items[secondApprovalId].Decision);
        Assert.Equal(2, fixture.Approvals.Items.Count);
    }

    [Fact]
    public async Task Only_the_doctor_linked_to_the_entry_can_decide()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        var otherDoctorUserId = Guid.NewGuid();
        fixture.Appointments.AddDoctor(Guid.NewGuid(), otherDoctorUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.DecideAsync(approvalId, otherDoctorUserId, approve: true, null, CancellationToken.None));

        Assert.Equal(WalkInAdmissionDecision.Pending, fixture.Approvals.Items[approvalId].Decision);
    }

    [Fact]
    public async Task Pending_request_expires_by_duration_and_is_recorded_as_expired_on_call()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        fixture.Clock.UtcNow = Start.AddMinutes(31);

        var result = await fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WalkInAdmissionDecision.Expired, fixture.Approvals.Items[approvalId].Decision);
        Assert.Equal(Start.AddMinutes(31), fixture.Approvals.Items[approvalId].DecisionAtUtc);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task Pending_request_expiry_is_capped_at_clinic_day_end()
    {
        var fixture = CreateFixture(approvalMinutes: 60);
        fixture.Clock.UtcNow = new DateTimeOffset(Today.Year, Today.Month, Today.Day, 23, 50, 0, TimeSpan.Zero);
        var entry = fixture.AddWalkIn(arrivedAt: fixture.Clock.UtcNow.AddMinutes(-1));

        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);

        Assert.Equal(fixture.Clock.ToUtc(Today.AddDays(1), TimeOnly.MinValue), fixture.Approvals.Items[approvalId].ExpiresAtUtc);
    }

    [Fact]
    public void Approval_expiry_option_defaults_to_thirty_minutes_and_rejects_nonpositive_values()
    {
        Assert.Equal(30, new WalkInAdmissionApprovalOptions().PendingDurationMinutes);
        var invalid = new WalkInAdmissionApprovalOptions { PendingDurationMinutes = 0 };

        Assert.Throws<ValidationException>(() => Validator.ValidateObject(invalid, new ValidationContext(invalid), validateAllProperties: true));
    }

    [Fact]
    public async Task Approval_from_yesterday_cannot_be_used()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        fixture.Clock.Today = Today.AddDays(1);
        fixture.Clock.UtcNow = fixture.Clock.ToUtc(fixture.Clock.Today, new TimeOnly(8, 0));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Approvals.Items[approvalId].UsedAtUtc);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task Approval_given_after_the_pending_window_stays_callable_for_the_rest_of_the_clinic_day()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        fixture.Clock.UtcNow = Start.AddMinutes(25);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        fixture.Clock.UtcNow = Start.AddHours(2).AddMinutes(25);

        var result = await fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(QueueEntryStatus.Called, entry.Status);
        Assert.Equal(Start.AddHours(2).AddMinutes(25), fixture.Approvals.Items[approvalId].UsedAtUtc);
    }

    [Fact]
    public async Task Approved_permit_is_not_callable_on_the_next_clinic_day()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        fixture.Clock.UtcNow = Start.AddMinutes(25);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        fixture.Clock.Today = Today.AddDays(1);
        fixture.Clock.UtcNow = fixture.Clock.ToUtc(Today.AddDays(1), new TimeOnly(0, 30));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Approvals.Items[approvalId].UsedAtUtc);
    }

    [Fact]
    public async Task Pending_request_still_expires_after_the_configured_duration_even_late_in_the_day()
    {
        var fixture = CreateFixture(approvalMinutes: 45);
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        fixture.Clock.UtcNow = Start.AddMinutes(46);

        var decision = await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);

        Assert.Contains("expired", decision, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WalkInAdmissionDecision.Expired, fixture.Approvals.Items[approvalId].Decision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None));
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task Explicit_call_is_refused_while_an_earlier_walk_in_holds_a_usable_approval()
    {
        var fixture = CreateFixture();
        var earlier = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-5), sequence: 1);
        var later = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-4), sequence: 2);
        var earlierApprovalId = await fixture.Service.RequestAsync(earlier.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(earlierApprovalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        var laterApprovalId = await fixture.Service.RequestAsync(later.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(laterApprovalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CallAsync(later.Id, laterApprovalId, CancellationToken.None));

        Assert.Contains("earlier approved walk-in", exception.Message);
        Assert.Equal(QueueEntryStatus.Waiting, later.Status);
        Assert.Null(fixture.Approvals.Items[laterApprovalId].UsedAtUtc);
    }

    [Fact]
    public async Task Explicit_call_ignores_an_earlier_walk_in_without_an_approval_or_with_a_rejection()
    {
        var fixture = CreateFixture();
        var unapproved = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-6), sequence: 1);
        var rejected = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-5), sequence: 2);
        var rejectionId = await fixture.Service.RequestAsync(rejected.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(rejectionId, fixture.DoctorUserId, approve: false, null, CancellationToken.None);
        var later = fixture.AddWalkIn(arrivedAt: Start.AddMinutes(-4), sequence: 3);
        var laterApprovalId = await fixture.Service.RequestAsync(later.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(laterApprovalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);

        var result = await fixture.Service.CallAsync(later.Id, laterApprovalId, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(QueueEntryStatus.Called, later.Status);
        Assert.Equal(QueueEntryStatus.Waiting, unapproved.Status);
        Assert.Equal(QueueEntryStatus.Waiting, rejected.Status);
    }

    [Fact]
    public async Task Approval_is_one_time_and_only_approved_request_can_call()
    {
        var fixture = CreateFixture();
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None));
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        var result = await fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(QueueEntryStatus.Called, entry.Status);
        Assert.Equal(Start, fixture.Approvals.Items[approvalId].UsedAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None));
    }

    [Theory]
    [InlineData(QueueEntryStatus.Called)]
    [InlineData(QueueEntryStatus.InConsultation)]
    public async Task Call_is_blocked_while_another_patient_is_called_or_in_consultation(QueueEntryStatus activeStatus)
    {
        var fixture = CreateFixture();
        var target = fixture.AddWalkIn(sequence: 2);
        var other = fixture.AddWalkIn(sequence: 1, arrivedAt: Start.AddMinutes(-1));
        var otherApproval = Approved(other, Start.AddMinutes(-2));
        other.CallWalkIn(otherApproval, Start.AddMinutes(-1), Today);
        if (activeStatus == QueueEntryStatus.InConsultation)
            other.StartConsultation(Start);
        var targetApprovalId = await fixture.Service.RequestAsync(target.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(targetApprovalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CallAsync(target.Id, targetApprovalId, CancellationToken.None));

        Assert.Contains("Called or InConsultation", exception.Message);
        Assert.Equal(QueueEntryStatus.Waiting, target.Status);
    }

    [Fact]
    public async Task Call_is_blocked_when_a_scheduled_patient_has_checked_in_and_is_waiting()
    {
        var fixture = CreateFixture();
        var target = fixture.AddWalkIn();
        fixture.Queue.Add(QueueEntry.CreateScheduled(Guid.NewGuid(), fixture.DoctorId, Guid.NewGuid(), Today, Start.AddMinutes(-10)));
        var approvalId = await fixture.Service.RequestAsync(target.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CallAsync(target.Id, approvalId, CancellationToken.None));

        Assert.Contains("scheduled patient", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(QueueEntryStatus.Waiting, target.Status);
    }

    [Fact]
    public async Task Call_is_allowed_for_replacement_while_scheduled_grace_runs_independently()
    {
        var fixture = CreateFixture();
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Request(patientId, fixture.DoctorId, Today, new TimeOnly(9, 0), new TimeOnly(9, 20), Start.AddHours(-1));
        appointment.Confirm(Start.AddHours(-1));
        appointment.StartGracePeriod(60, fixture.Clock.ToUtc(Today, new TimeOnly(9, 0)));
        fixture.Appointments.Appointments[appointment.Id] = appointment;
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);

        var result = await fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None);
        entry.StartConsultation(Start.AddMinutes(1));

        fixture.Clock.UtcNow = fixture.Clock.ToUtc(Today, new TimeOnly(9, 20));
        var checkIn = new CheckInService(fixture.Appointments, fixture.Queue, new FakeExpectedDurationService(), new InMemoryDoctorDisruptionRepository(), fixture.UnitOfWork, fixture.Clock,
            new NoopGracePeriodService(), new DoctorCallWindowService(fixture.Appointments, new InMemoryDoctorDisruptionRepository(), new InMemoryDoctorScheduleRepository()));
        var scheduledResult = await checkIn.CheckInScheduledPatientAsync(appointment.Id, fixture.DoctorId, CancellationToken.None);

        Assert.True(result.Called);
        Assert.False(scheduledResult.RequiresWalkInRegistration);
        var scheduledEntry = fixture.Queue.Entries[scheduledResult.QueueEntryId!.Value];
        Assert.Equal(QueueEntrySource.Scheduled, scheduledEntry.Source);
        Assert.Null(scheduledEntry.QueueSequence);
        Assert.Equal(QueueEntryStatus.Waiting, scheduledEntry.Status);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(fixture.Clock.ToUtc(Today, new TimeOnly(9, 0)), appointment.GracePeriodStartedAtUtc);
        Assert.Equal(60, appointment.FrozenGracePeriodMinutes);
        Assert.False(appointment.IsGracePeriodExpired(fixture.Clock.UtcNow));
        Assert.Equal(QueueEntryStatus.InConsultation, entry.Status);
    }

    [Fact]
    public async Task Scenario_B_no_show_arrival_is_a_new_walk_in_and_still_needs_its_own_approval()
    {
        var fixture = CreateFixture();
        fixture.Clock.UtcNow = Start.AddMinutes(2);
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Request(patientId, fixture.DoctorId, Today, new TimeOnly(9, 0), new TimeOnly(9, 20), Start.AddHours(-1));
        appointment.Confirm(Start.AddHours(-1));
        appointment.MarkNoShow(Start);
        fixture.Appointments.Appointments.Add(appointment.Id, appointment);
        var walkIn = QueueEntry.CreateWalkIn(patientId, fixture.DoctorId, Today, fixture.Clock.UtcNow.AddMinutes(-1), 1);
        fixture.Queue.Add(walkIn);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CallAsync(walkIn.Id, Guid.NewGuid(), CancellationToken.None));
        var approvalId = await fixture.Service.RequestAsync(walkIn.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        var result = await fixture.Service.CallAsync(walkIn.Id, approvalId, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Null(walkIn.AppointmentId);
        Assert.Equal(QueueEntryStatus.Called, walkIn.Status);
    }

    [Fact]
    public async Task Call_is_rejected_after_doctors_configured_working_hours()
    {
        var fixture = CreateFixture(approvalMinutes: 600);
        var entry = fixture.AddWalkIn();
        var approvalId = await fixture.Service.RequestAsync(entry.Id, CancellationToken.None);
        await fixture.Service.DecideAsync(approvalId, fixture.DoctorUserId, approve: true, null, CancellationToken.None);
        fixture.Clock.UtcNow = fixture.Clock.ToUtc(Today, new TimeOnly(17, 1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CallAsync(entry.Id, approvalId, CancellationToken.None));

        Assert.Contains("working hours", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Approvals.Items[approvalId].UsedAtUtc);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    private static Fixture CreateFixture(int approvalMinutes = 30)
    {
        var appointments = new FakeAppointmentRepository();
        var queue = new FakeQueueRepository();
        var approvals = new FakeApprovalRepository();
        var unitOfWork = new FakeUnitOfWork();
        var clock = new FakeClinicClock(Start, Today);
        var doctorId = Guid.NewGuid();
        var doctorUserId = Guid.NewGuid();
        appointments.AddDoctor(doctorId, doctorUserId);
        foreach (var day in Enum.GetValues<DayOfWeek>())
            appointments.WorkingHours.Add(DoctorWorkingHour.Create(doctorId, day, new TimeOnly(8, 0), new TimeOnly(17, 0)));
        var schedule = new InMemoryDoctorScheduleRepository();
        var disruptions = new InMemoryDoctorDisruptionRepository();
        var service = new WalkInAdmissionApprovalService(queue, appointments, approvals, unitOfWork, clock,
            Options.Create(new WalkInAdmissionApprovalOptions { PendingDurationMinutes = approvalMinutes }),
            new DoctorAvailabilityService(appointments, schedule, clock),
            new DoctorCallWindowService(appointments, disruptions, schedule));
        return new Fixture(service, appointments, queue, approvals, unitOfWork, clock, doctorId, doctorUserId);
    }

    private static WalkInAdmissionApproval Approved(QueueEntry entry, DateTimeOffset requestedAt)
    {
        var approval = WalkInAdmissionApproval.Request(entry, requestedAt, requestedAt.AddHours(1));
        approval.Approve(requestedAt.AddSeconds(1));
        return approval;
    }

    private sealed record Fixture(WalkInAdmissionApprovalService Service, FakeAppointmentRepository Appointments,
        FakeQueueRepository Queue, FakeApprovalRepository Approvals, FakeUnitOfWork UnitOfWork, FakeClinicClock Clock,
        Guid DoctorId, Guid DoctorUserId)
    {
        public QueueEntry AddWalkIn(DateTimeOffset? arrivedAt = null, long sequence = 1)
        {
            var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), DoctorId, Today, arrivedAt ?? Start.AddMinutes(-1), sequence);
            Queue.Add(entry);
            return entry;
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

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        public Dictionary<Guid, Doctor> Doctors { get; } = [];
        public Dictionary<Guid, Appointment> Appointments { get; } = [];
        public List<DoctorWorkingHour> WorkingHours { get; } = [];
        public void AddDoctor(Guid doctorId, Guid userId) => Doctors[doctorId] = new Doctor(doctorId, userId, "Dr. Test", Guid.NewGuid(), 20);
        public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken ct) => Task.FromResult(Doctors.GetValueOrDefault(doctorId));
        public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken ct) => Task.FromResult(Doctors.Values.SingleOrDefault(doctor => doctor.ApplicationUserId == applicationUserId));
        public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken ct) => Task.FromResult<IReadOnlyList<DoctorWorkingHour>>(WorkingHours.Where(item => item.DoctorId == doctorId && item.DayOfWeek == dayOfWeek).ToArray());
        public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken ct) => Task.FromResult(Appointments.GetValueOrDefault(appointmentId));
        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<Appointment>>(Appointments.Values.Where(a => a.DoctorId == doctorId && a.AppointmentDate == date && a.Status == AppointmentStatus.Confirmed).ToArray());
        public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult(Appointments.Values.Any(a => a.PatientId == patientId && a.DoctorId == doctorId && a.AppointmentDate == date && a.Status == AppointmentStatus.Confirmed));
        public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(Appointment appointment, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<Appointment>> GetLiveForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken ct) => Task.FromResult<IReadOnlyList<Appointment>>([]);
    public Task<bool> HasLivePatientOverlapAsync(Guid patientId, DateOnly date, TimeOnly start, TimeOnly end, IReadOnlyCollection<Guid> excludedAppointmentIds, CancellationToken ct) => Task.FromResult(false);
    public Task<Appointment?> GetLiveReplacementAsync(Guid originalAppointmentId, CancellationToken ct) => Task.FromResult<Appointment?>(null);
        public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Appointment>>([]);
    }

    private sealed class FakeQueueRepository : IQueueRepository
    {
        private readonly Dictionary<Guid, QueueEntry> entries = [];
        public Dictionary<Guid, QueueEntry> Entries => entries;
        public void Add(QueueEntry entry) => entries.Add(entry.Id, entry);
        public Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(entries.GetValueOrDefault(id));
        public Task<bool> HasLiveQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken ct) => Task.FromResult(entries.Values.Any(item => item.AppointmentId == appointmentId && item.Status.IsLive()));
        public Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueueEntry>>(entries.Values.Where(item => item.DoctorId == doctorId && item.QueueDate == date && item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation).OrderBy(item => item.ArrivedAt).ThenBy(item => item.QueueSequence).ToArray());
        public Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueueEntry>>(entries.Values.Where(item => item.DoctorId == doctorId && item.QueueDate == date && item.Source == QueueEntrySource.WalkIn && item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation).OrderBy(item => item.ArrivedAt).ThenBy(item => item.QueueSequence).ToArray());
        public Task AddAsync(QueueEntry queueEntry, CancellationToken ct) { entries.Add(queueEntry.Id, queueEntry); return Task.CompletedTask; }
        public Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult(entries.Values.Any(item => item.PatientId == patientId && item.DoctorId == doctorId && item.QueueDate == date && item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation));
        public Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult(entries.Values.Where(item => item.DoctorId == doctorId && item.QueueDate == date && item.QueueSequence != null).Select(item => item.QueueSequence!.Value).DefaultIfEmpty(0).Max() + 1);
        public Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct) => Task.FromResult(entries.Values.SingleOrDefault(item => item.AppointmentId == appointmentId));
        public Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueueEntry>>(entries.Values.Where(item => item.DoctorId == doctorId && item.QueueDate == date && item.Source == QueueEntrySource.Scheduled && item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation).OrderBy(item => item.Appointment?.StartTime).ToArray());
        public Task<IReadOnlyList<QueueEntry>> GetLiveEntriesBeforeDateAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueueEntry>>([]);
        public Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueueEntry>>([]);
    }

    private sealed class FakeApprovalRepository : IWalkInAdmissionApprovalRepository
    {
        public Dictionary<Guid, WalkInAdmissionApproval> Items { get; } = [];
        public Task<bool> HasPendingForQueueEntryAsync(Guid queueEntryId, CancellationToken ct) => Task.FromResult(Items.Values.Any(item => item.QueueEntryId == queueEntryId && item.Decision == WalkInAdmissionDecision.Pending));
        public Task AddAsync(WalkInAdmissionApproval approval, CancellationToken ct) { Items.Add(approval.Id, approval); return Task.CompletedTask; }
        public Task<WalkInAdmissionApproval?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task<IReadOnlyList<WalkInAdmissionApproval>> GetPendingForDoctorAsync(Guid doctorId, CancellationToken ct) => Task.FromResult<IReadOnlyList<WalkInAdmissionApproval>>(Items.Values.Where(item => item.DoctorId == doctorId && item.Decision == WalkInAdmissionDecision.Pending).OrderBy(item => item.RequestedAtUtc).ToArray());
        public Task<IReadOnlyList<WalkInAdmissionApproval>> GetForQueueEntriesAsync(IReadOnlyCollection<Guid> queueEntryIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<WalkInAdmissionApproval>>(Items.Values.Where(item => queueEntryIds.Contains(item.QueueEntryId)).OrderBy(item => item.RequestedAtUtc).ToArray());
    }

    private sealed class FakeExpectedDurationService : IExpectedDurationService
    {
        public Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
            Task.FromResult(new ExpectedDurationResult(20, false, 0, null));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) => Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) => ExecuteAsync(operation, ct);
        public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly date, Func<CancellationToken, Task<T>> operation, CancellationToken ct) => ExecuteAsync(operation, ct);
        public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly date, Func<CancellationToken, Task<T>> operation, CancellationToken ct) => ExecuteAsync(operation, ct);
        private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        {
            await using var transaction = await BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await operation(ct);
            await SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        private sealed class FakeTransaction : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
