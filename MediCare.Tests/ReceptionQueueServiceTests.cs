using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class ReceptionQueueServiceTests
{
    private static readonly DateOnly Today = QueueTestData.Today;

    [Fact]
    public async Task Call_scheduled_calls_a_waiting_checked_in_patient_when_the_doctor_is_free()
    {
        var s = new QueueScenario();
        var entry = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("A"), new TimeOnly(9, 30)));

        await s.Service.CallScheduledAsync(entry.Id, CancellationToken.None);

        Assert.Equal(QueueEntryStatus.Called, entry.Status);
        Assert.Equal(s.Clock.UtcNow, entry.CalledAt);
    }

    [Fact]
    public async Task Call_scheduled_refuses_while_an_earlier_scheduled_patient_is_waiting()
    {
        var s = new QueueScenario();
        var early = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Early"), new TimeOnly(9, 0)));
        var late = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Late"), new TimeOnly(10, 0)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CallScheduledAsync(late.Id, CancellationToken.None));

        Assert.Contains("earlier scheduled patient", exception.Message);
        Assert.Equal(QueueEntryStatus.Waiting, early.Status);
        Assert.Equal(QueueEntryStatus.Waiting, late.Status);
    }

    [Fact]
    public async Task Call_scheduled_refuses_while_the_doctor_is_busy()
    {
        var s = new QueueScenario();
        var first = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("First"), new TimeOnly(9, 0)));
        var second = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Second"), new TimeOnly(9, 30)));
        first.Call(s.Clock.UtcNow);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CallScheduledAsync(second.Id, CancellationToken.None));

        Assert.Contains("Doctor is busy", exception.Message);
        Assert.Equal(QueueEntryStatus.Waiting, second.Status);
    }

    [Fact]
    public async Task Call_next_picks_the_earliest_checked_in_scheduled_patient_even_when_a_walk_in_arrived_first()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-10), 1);
        s.Approve(walkIn);
        var scheduled = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Scheduled"), new TimeOnly(9, 0)));

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(scheduled.Id, result.QueueEntryId);
        Assert.Equal(QueueEntryStatus.Called, scheduled.Status);
        Assert.Equal(QueueEntryStatus.Waiting, walkIn.Status);
        Assert.Null(s.Approvals.Items.Values.Single().UsedAtUtc);
    }

    [Fact]
    public async Task Call_next_moves_to_a_walk_in_only_when_no_scheduled_patient_is_checked_in()
    {
        var s = new QueueScenario();
        s.AddConfirmedAppointment(s.AddPatient("Not arrived yet"), new TimeOnly(9, 30));
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        var approval = s.Approve(walkIn);

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(walkIn.Id, result.QueueEntryId);
        Assert.Equal(QueueEntryStatus.Called, walkIn.Status);
        Assert.Equal(s.Clock.UtcNow, approval.UsedAtUtc);
    }

    [Fact]
    public async Task Call_next_skips_an_unapproved_fifo_head_and_calls_the_earliest_approved_walk_in()
    {
        var s = new QueueScenario();
        var head = s.AddWalkIn(s.AddPatient("Head"), s.Clock.UtcNow.AddMinutes(-15), 1);
        var later = s.AddWalkIn(s.AddPatient("Later"), s.Clock.UtcNow.AddMinutes(-10), 2);
        s.Approve(later);

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(later.Id, result.QueueEntryId);
        Assert.Equal(QueueEntryStatus.Waiting, head.Status);
        Assert.Equal(QueueEntryStatus.Called, later.Status);
    }

    [Fact]
    public async Task Call_next_reports_when_no_walk_in_is_approved_and_counts_those_waiting_for_approval()
    {
        var s = new QueueScenario();
        var head = s.AddWalkIn(s.AddPatient("Head"), s.Clock.UtcNow.AddMinutes(-15), 1);
        var later = s.AddWalkIn(s.AddPatient("Later"), s.Clock.UtcNow.AddMinutes(-10), 2);

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Null(result.QueueEntryId);
        Assert.Equal("No approved walk-in is waiting; 2 walk-ins need the doctor's approval.", result.Message);
        Assert.Equal(QueueEntryStatus.Waiting, head.Status);
        Assert.Equal(QueueEntryStatus.Waiting, later.Status);
    }

    [Fact]
    public async Task Board_estimates_put_approved_walk_ins_first_in_call_order_and_keep_fifo_within_each_group()
    {
        var s = new QueueScenario();
        var head = s.AddWalkIn(s.AddPatient("Head"), s.Clock.UtcNow.AddMinutes(-15), 1);
        var later = s.AddWalkIn(s.AddPatient("Later"), s.Clock.UtcNow.AddMinutes(-10), 2);
        s.Approve(later);

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        var headRow = board.WalkIns.Single(row => row.QueueEntryId == head.Id);
        var laterRow = board.WalkIns.Single(row => row.QueueEntryId == later.Id);
        Assert.True(laterRow.EstimatedStartUtc < headRow.EstimatedStartUtc);
    }

    [Fact]
    public async Task Call_next_keeps_a_rejected_walk_in_in_fifo_position_and_calls_the_approved_one_behind_it()
    {
        var s = new QueueScenario();
        var rejected = s.AddWalkIn(s.AddPatient("Rejected"), s.Clock.UtcNow.AddMinutes(-15), 1);
        var rejectedApproval = s.Reject(rejected);
        var approved = s.AddWalkIn(s.AddPatient("Approved"), s.Clock.UtcNow.AddMinutes(-10), 2);
        s.Approve(approved);

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(approved.Id, result.QueueEntryId);
        Assert.Equal(WalkInAdmissionDecision.Rejected, rejectedApproval.Decision);
        Assert.Equal(QueueEntryStatus.Waiting, rejected.Status);
    }

    [Fact]
    public async Task Call_next_reports_a_busy_doctor_without_calling_anyone()
    {
        var s = new QueueScenario();
        var active = s.AddWalkIn(s.AddPatient("Active"), s.Clock.UtcNow.AddMinutes(-20), 1);
        s.CallWalkIn(active);
        var waiting = s.AddWalkIn(s.AddPatient("Waiting"), s.Clock.UtcNow.AddMinutes(-10), 2);
        s.Approve(waiting);

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Contains("Doctor is busy", result.Message);
        Assert.Equal(QueueEntryStatus.Waiting, waiting.Status);
    }

    [Fact]
    public async Task Call_next_refuses_a_walk_in_outside_configured_working_hours_with_a_failure_result()
    {
        var s = new QueueScenario(workingHours: false);
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.Approve(walkIn);

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Contains("outside configured working hours", result.Message);
        Assert.Equal(QueueEntryStatus.Waiting, walkIn.Status);
    }

    [Fact]
    public async Task Call_next_reports_an_empty_queue()
    {
        var s = new QueueScenario();

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Null(result.QueueEntryId);
    }

    [Fact]
    public async Task Returning_a_walk_in_to_waiting_keeps_the_used_approval_so_a_new_one_is_needed()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.Approve(walkIn);
        await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        var message = await s.Service.ReturnToWaitingAsync(walkIn.Id, CancellationToken.None);
        var retry = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.Contains("new approval is needed", message);
        Assert.Equal(QueueEntryStatus.Waiting, walkIn.Status);
        Assert.False(retry.Called);
        Assert.Equal("No approved walk-in is waiting; 1 walk-ins need the doctor's approval.", retry.Message);
    }

    [Fact]
    public async Task Patient_leaving_cancels_the_attempt_and_keeps_the_appointment_confirmed_for_a_new_check_in()
    {
        var s = new QueueScenario();
        var appointment = s.AddConfirmedAppointment(s.AddPatient("Leaver"), new TimeOnly(9, 0));
        var entry = s.CheckIn(appointment);

        var message = await s.Service.CancelQueueEntryAsync(entry.Id, CancellationToken.None);

        Assert.Equal(QueueEntryStatus.Cancelled, entry.Status);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Contains("may check in again", message);
    }

    [Fact]
    public async Task Reception_cancel_records_the_clock_now_as_the_close_time()
    {
        var s = new QueueScenario();
        var appointment = s.AddConfirmedAppointment(s.AddPatient("Leaver"), new TimeOnly(9, 0));
        var entry = s.CheckIn(appointment);

        await s.Service.CancelQueueEntryAsync(entry.Id, CancellationToken.None);

        Assert.Equal(s.Clock.UtcNow, entry.ClosedAtUtc);
    }

    [Fact]
    public async Task Find_patient_by_phone_normalizes_input_and_reports_unknown_numbers()
    {
        var s = new QueueScenario();

        var invalid = await s.Service.FindPatientByPhoneAsync("12345", CancellationToken.None);
        var unknown = await s.Service.FindPatientByPhoneAsync("01012345678", CancellationToken.None);

        Assert.Null(invalid.PatientId);
        Assert.False(invalid.NotRegistered);
        Assert.NotNull(invalid.Error);
        Assert.Null(unknown.PatientId);
        Assert.True(unknown.NotRegistered);
    }
}

/// <summary>Builds one doctor's day with a fixed clock (09:00 UTC on the clinic date, equal to clinic time in tests).</summary>
internal sealed class QueueScenario
{
    public QueueScenario(bool workingHours = true, IExpectedDurationService? expectedDuration = null)
    {
        Clock = new FixedClock(QueueTestData.Noon, QueueTestData.Today);
        Queue = new InMemoryQueueRepository();
        Appointments = new InMemoryAppointmentRepository();
        Approvals = new InMemoryApprovalRepository();
        Board = new InMemoryQueueBoardRepository(Appointments, Queue);
        UnitOfWork = new InMemoryUnitOfWork();
        Doctor = QueueTestData.NewDoctor();
        Appointments.AddDoctor(Doctor);
        if (workingHours)
            Appointments.WorkingHours.Add(DoctorWorkingHour.Create(Doctor.Id, QueueTestData.Today.DayOfWeek, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        Disruptions = new InMemoryDoctorDisruptionRepository();
        Schedule = new InMemoryDoctorScheduleRepository();
        Service = new ReceptionQueueService(Queue, Appointments, Approvals, Board, new NoopGracePeriodService(), UnitOfWork, Clock,
            new DoctorAvailabilityService(Appointments, Schedule, Clock), new NoopDisruptionService(),
            new DoctorCallWindowService(Appointments, Disruptions, Schedule));
        Consultations = new ConsultationService(Queue, Appointments, UnitOfWork, Clock);
        BoardService = new QueueBoardService(Board, Appointments, Approvals, expectedDuration ?? new FixedExpectedDuration(20), Clock, new InMemoryDoctorDisruptionRepository());
    }

    public ReceptionQueueService Service { get; }
    public ConsultationService Consultations { get; }
    public QueueBoardService BoardService { get; }
    public Guid DoctorUserId => Doctor.ApplicationUserId;

    public FixedClock Clock { get; }
    public InMemoryQueueRepository Queue { get; }
    public InMemoryAppointmentRepository Appointments { get; }
    public InMemoryApprovalRepository Approvals { get; }
    public InMemoryQueueBoardRepository Board { get; }
    public InMemoryUnitOfWork UnitOfWork { get; }
    public Doctor Doctor { get; }
    public InMemoryDoctorDisruptionRepository Disruptions { get; }
    public InMemoryDoctorScheduleRepository Schedule { get; }

    private readonly Dictionary<Guid, Patient> patients = [];
    private readonly Dictionary<Guid, Patient> patientByAppointment = [];

    public Patient AddPatient(string name)
    {
        var patient = QueueTestData.NewPatient(name);
        patients[patient.Id] = patient;
        return patient;
    }

    public Appointment AddConfirmedAppointment(Patient patient, TimeOnly start)
    {
        var appointment = Appointment.Request(patient.Id, Doctor.Id, QueueTestData.Today, start, start.AddMinutes(20), Clock.UtcNow);
        appointment.Confirm(Clock.UtcNow);
        QueueTestData.LinkAppointment(appointment, patient);
        Appointments.Appointments[appointment.Id] = appointment;
        patientByAppointment[appointment.Id] = patient;
        return appointment;
    }

    public QueueEntry CheckIn(Appointment appointment)
    {
        var patient = patientByAppointment[appointment.Id];
        var entry = QueueEntry.CreateScheduled(patient.Id, Doctor.Id, appointment.Id, QueueTestData.Today, Clock.UtcNow);
        QueueTestData.Link(entry, patient, appointment);
        Queue.Add(entry);
        return entry;
    }

    public QueueEntry AddWalkIn(Patient patient, DateTimeOffset arrivedAt, long sequence)
    {
        var entry = QueueEntry.CreateWalkIn(patient.Id, Doctor.Id, QueueTestData.Today, arrivedAt, sequence);
        QueueTestData.Link(entry, patient);
        Queue.Add(entry);
        return entry;
    }

    /// <summary>Requests and approves an admission for a walk-in, as Reception and the doctor would.</summary>
    public WalkInAdmissionApproval Approve(QueueEntry walkIn)
    {
        var approval = WalkInAdmissionApproval.Request(walkIn, Clock.UtcNow.AddMinutes(-1), Clock.UtcNow.AddHours(1));
        approval.Approve(Clock.UtcNow.AddSeconds(-30));
        Approvals.Items[approval.Id] = approval;
        return approval;
    }

    public DoctorDisruption AddDisruption(TimeOnly fromTime)
    {
        var disruption = DoctorDisruption.Record(Doctor.Id, QueueTestData.Today, fromTime, "Emergency", Guid.NewGuid(), Clock.UtcNow);
        Disruptions.Disruptions[disruption.Id] = disruption;
        return disruption;
    }

    public WalkInAdmissionApproval Reject(QueueEntry walkIn)
    {
        var approval = WalkInAdmissionApproval.Request(walkIn, Clock.UtcNow.AddMinutes(-1), Clock.UtcNow.AddHours(1));
        approval.Reject(Clock.UtcNow.AddSeconds(-30));
        Approvals.Items[approval.Id] = approval;
        return approval;
    }

    /// <summary>Calls a walk-in through the real domain rule with its approval, for tests that need an active consultation.</summary>
    public void CallWalkIn(QueueEntry walkIn)
    {
        var approval = Approve(walkIn);
        walkIn.CallWalkIn(approval, Clock.UtcNow, QueueTestData.Today);
    }
}
