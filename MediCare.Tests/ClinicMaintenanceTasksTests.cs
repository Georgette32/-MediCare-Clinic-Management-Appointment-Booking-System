using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Maintenance housekeeping: stale queue sweep and per-doctor grace processing, each isolated per unit of work.</summary>
public sealed class ClinicMaintenanceTasksTests
{
    private static readonly DateOnly Today = QueueTestData.Today;
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Fact]
    public async Task Sweep_cancels_waiting_and_called_entries_from_earlier_days_and_withdraws_their_pending_approvals()
    {
        var w = new World(At(10, 5));
        var waiting = QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Yesterday, At(8, 0), 1);
        var pendingApproval = WalkInAdmissionApproval.Request(waiting, At(8, 1), At(8, 31));
        w.Queue.Add(waiting);
        w.Approvals.Items[pendingApproval.Id] = pendingApproval;
        var called = QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Yesterday, At(8, 2), 2);
        var calledApproval = WalkInAdmissionApproval.Request(called, At(8, 3), At(8, 33));
        calledApproval.Approve(At(8, 4));
        called.CallWalkIn(calledApproval, At(8, 5), Yesterday);
        w.Queue.Add(called);
        w.Approvals.Items[calledApproval.Id] = calledApproval;

        var result = await w.Tasks.SweepStaleQueueEntriesAsync(CancellationToken.None);

        Assert.Equal(2, result.Cancelled);
        Assert.Equal(0, result.Completed);
        Assert.Empty(result.Failures);
        Assert.Equal(QueueEntryStatus.Cancelled, waiting.Status);
        Assert.Equal(QueueEntryStatus.Cancelled, called.Status);
        Assert.Equal(WalkInAdmissionDecision.Expired, pendingApproval.Decision);
    }

    [Fact]
    public async Task Sweep_completes_in_consultation_entries_and_marks_their_scheduled_appointment_completed()
    {
        var w = new World(At(10, 5));
        var patient = QueueTestData.NewPatient("Yesterday");
        var appointment = Appointment.Request(patient.Id, w.Doctor.Id, Yesterday, new TimeOnly(9, 0), new TimeOnly(9, 20), At(7, 0));
        appointment.Confirm(At(7, 0));
        w.Appointments.Appointments[appointment.Id] = appointment;
        var entry = QueueEntry.CreateScheduled(patient.Id, w.Doctor.Id, appointment.Id, Yesterday, At(8, 55));
        QueueTestData.Link(entry, patient, appointment);
        entry.Call(At(9, 0));
        entry.StartConsultation(At(9, 2));
        w.Queue.Add(entry);

        var result = await w.Tasks.SweepStaleQueueEntriesAsync(CancellationToken.None);

        Assert.Equal(0, result.Cancelled);
        Assert.Equal(1, result.Completed);
        Assert.Equal(QueueEntryStatus.Completed, entry.Status);
        Assert.Equal(At(10, 5), entry.CompletedAt);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    [Fact]
    public async Task Sweep_leaves_todays_entries_alone()
    {
        var w = new World(At(10, 5));
        var today = QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Today, At(9, 30), 1);
        w.Queue.Add(today);

        var result = await w.Tasks.SweepStaleQueueEntriesAsync(CancellationToken.None);

        Assert.Equal(0, result.Cancelled + result.Completed);
        Assert.Equal(QueueEntryStatus.Waiting, today.Status);
    }

    [Fact]
    public async Task A_failing_entry_is_reported_and_does_not_stop_the_rest_of_the_sweep()
    {
        var w = new World(At(10, 5));
        var patient = QueueTestData.NewPatient("Broken");
        var appointment = Appointment.Request(patient.Id, w.Doctor.Id, Yesterday, new TimeOnly(9, 0), new TimeOnly(9, 20), At(7, 0));
        appointment.Confirm(At(7, 0));
        w.Appointments.Appointments[appointment.Id] = appointment;
        var broken = QueueEntry.CreateScheduled(patient.Id, w.Doctor.Id, appointment.Id, Yesterday, At(8, 55));
        QueueTestData.Link(broken, patient, appointment);
        broken.Call(At(9, 0));
        // Started at the sweep time itself, so completion cannot follow the start and the domain refuses it.
        broken.StartConsultation(At(10, 5));
        w.Queue.Add(broken);
        var waiting = QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Yesterday, At(8, 0), 1);
        w.Queue.Add(waiting);

        var result = await w.Tasks.SweepStaleQueueEntriesAsync(CancellationToken.None);

        Assert.Equal(1, result.Cancelled);
        Assert.Equal(0, result.Completed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains(broken.Id.ToString("D"), failure);
        Assert.Equal(QueueEntryStatus.InConsultation, broken.Status);
        Assert.Equal(QueueEntryStatus.Cancelled, waiting.Status);
    }

    [Fact]
    public async Task Grace_processing_isolates_a_doctor_whose_run_fails()
    {
        var w = new World(At(10, 5));
        var failing = QueueTestData.NewDoctor();
        w.Appointments.AddDoctor(failing);
        var grace = new FakeGracePeriods(failingDoctorId: failing.Id);
        var tasks = new ClinicMaintenanceTasks(new InMemoryQueueBoardRepository(w.Appointments, w.Queue), grace, w.Queue,
            w.Approvals, new InMemoryUnitOfWork(), w.Clock, new ScheduleWorld());

        var result = await tasks.ProcessGracePeriodsForActiveDoctorsAsync(CancellationToken.None);

        Assert.Equal(1, result.DoctorsProcessed);
        Assert.Equal(1, result.Changes);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("boom", failure);
    }

    [Fact]
    public async Task Grace_processing_turns_an_expired_grace_period_into_no_show_for_each_active_doctor()
    {
        var w = new World(At(10, 5));
        var patient = QueueTestData.NewPatient("Late");
        var appointment = Appointment.Request(patient.Id, w.Doctor.Id, Today, new TimeOnly(9, 0), new TimeOnly(9, 20), At(7, 0));
        appointment.Confirm(At(7, 0));
        w.Appointments.Appointments[appointment.Id] = appointment;
        var grace = new GracePeriodService(w.Appointments, w.Queue, new FixedExpectedDuration(20), new InMemoryDoctorDisruptionRepository(),
            new InMemoryUnitOfWork(), w.Clock);
        var tasks = new ClinicMaintenanceTasks(new InMemoryQueueBoardRepository(w.Appointments, w.Queue), grace, w.Queue,
            w.Approvals, new InMemoryUnitOfWork(), w.Clock, new ScheduleWorld());

        var result = await tasks.ProcessGracePeriodsForActiveDoctorsAsync(CancellationToken.None);

        Assert.Empty(result.Failures);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Equal(2, result.Changes); // grace started, then expired to No-Show
    }

    [Fact]
    public async Task Due_duration_change_becomes_the_current_length_and_a_future_one_waits()
    {
        var w = new World(At(10, 5));
        var due = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Due", Guid.NewGuid(), 30);
        due.ScheduleDurationChange(35, Today, Today.AddDays(-1));
        var future = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Future", Guid.NewGuid(), 30);
        future.ScheduleDurationChange(40, Today.AddDays(1), Today);
        w.Schedule.Doctors.Add(due);
        w.Schedule.Doctors.Add(future);

        var result = await w.Tasks.PromoteDueDurationChangesAsync(CancellationToken.None);

        Assert.Equal(1, result.Promoted);
        Assert.Empty(result.Failures);
        Assert.Equal(35, due.AppointmentDurationMinutes);
        Assert.Null(due.NextAppointmentDurationMinutes);
        Assert.Equal(30, future.AppointmentDurationMinutes);
        Assert.Equal(40, future.NextAppointmentDurationMinutes);
    }

    private static DateTimeOffset At(int hour, int minute) => new(Today.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private sealed class World
    {
        public ScheduleWorld Schedule { get; } = new();

        public World(DateTimeOffset now)
        {
            Clock = new FixedClock(now, Today);
            Doctor = QueueTestData.NewDoctor();
            Appointments.AddDoctor(Doctor);
            Tasks = new ClinicMaintenanceTasks(new InMemoryQueueBoardRepository(Appointments, Queue), new FakeGracePeriods(null), Queue,
                Approvals, new InMemoryUnitOfWork(), Clock, Schedule);
        }

        public FixedClock Clock { get; }
        public Doctor Doctor { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public InMemoryApprovalRepository Approvals { get; } = new();
        public ClinicMaintenanceTasks Tasks { get; }
    }

    private sealed class FakeGracePeriods(Guid? failingDoctorId) : IGracePeriodService
    {
        public Task<GracePeriodStartResult> StartGracePeriodIfEligibleAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ProcessExpiredGracePeriodsForDoctorAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> ProcessDoctorDayAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken)
        {
            if (doctorId == failingDoctorId)
                throw new InvalidOperationException("boom");
            return Task.FromResult(1);
        }
    }
}
