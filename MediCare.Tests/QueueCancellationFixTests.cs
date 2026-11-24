using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Regression tests for the queue fixes found in review: appointment cancellation releases a Waiting entry, and a cancelled walk-in withdraws its pending approval.</summary>
public sealed class QueueCancellationFixTests
{
    private static readonly DateOnly Date = new(2026, 12, 2);
    private static readonly DateTimeOffset Now = new(2026, 12, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Clinic_cancellation_cancels_a_waiting_queue_entry_in_the_same_transaction()
    {
        var s = Fixture.Create();
        var appointment = s.AddConfirmedAppointment(new TimeOnly(9, 0));
        var entry = QueueEntry.CreateScheduled(s.PatientId, s.DoctorId, appointment.Id, Date, Now);
        s.Queue.Add(entry);
        var commitsBefore = s.UnitOfWork.CommitCount;

        await s.Booking.CancelByClinicAsync(appointment.Id, ClinicCancellationActor.Reception, "Doctor unavailable", CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(QueueEntryStatus.Cancelled, entry.Status);
        Assert.Equal(commitsBefore + 1, s.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Clinic_cancellation_is_refused_when_the_patient_is_already_called()
    {
        var s = Fixture.Create();
        var appointment = s.AddConfirmedAppointment(new TimeOnly(9, 0));
        var entry = QueueEntry.CreateScheduled(s.PatientId, s.DoctorId, appointment.Id, Date, Now);
        entry.Call(Now.AddMinutes(1));
        s.Queue.Add(entry);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.Booking.CancelByClinicAsync(appointment.Id, ClinicCancellationActor.Reception, "Doctor unavailable", CancellationToken.None));

        Assert.Equal("The patient is already being served.", error.Message);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(QueueEntryStatus.Called, entry.Status);
    }

    [Fact]
    public async Task Clinic_cancellation_is_refused_when_the_patient_is_in_consultation()
    {
        var s = Fixture.Create();
        var appointment = s.AddConfirmedAppointment(new TimeOnly(9, 0));
        var entry = QueueEntry.CreateScheduled(s.PatientId, s.DoctorId, appointment.Id, Date, Now);
        entry.Call(Now.AddMinutes(1));
        entry.StartConsultation(Now.AddMinutes(2));
        s.Queue.Add(entry);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.Booking.CancelByClinicAsync(appointment.Id, ClinicCancellationActor.Admin, "Doctor unavailable", CancellationToken.None));

        Assert.Equal("The patient is already being served.", error.Message);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(QueueEntryStatus.InConsultation, entry.Status);
    }

    [Fact]
    public async Task Clinic_rescheduling_cancels_a_waiting_queue_entry_for_the_original_appointment()
    {
        var s = Fixture.Create();
        var appointment = s.AddConfirmedAppointment(new TimeOnly(9, 0));
        var entry = QueueEntry.CreateScheduled(s.PatientId, s.DoctorId, appointment.Id, Date, Now);
        s.Queue.Add(entry);

        var replacementId = await s.Booking.RescheduleByClinicAsync(
            appointment.Id, ClinicCancellationActor.Reception, s.DoctorId, Date, new TimeOnly(9, 20), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Rescheduled, appointment.Status);
        Assert.Equal(AppointmentStatus.Confirmed, s.Appointments.Appointments[replacementId].Status);
        Assert.Equal(QueueEntryStatus.Cancelled, entry.Status);
    }

    [Fact]
    public async Task Cancelling_a_walk_in_withdraws_its_pending_admission_request()
    {
        var s = Fixture.Create();
        var entry = QueueEntry.CreateWalkIn(s.PatientId, s.DoctorId, Date, Now.AddMinutes(-3), 1);
        s.Queue.Add(entry);
        var approval = WalkInAdmissionApproval.Request(entry, Now.AddMinutes(-3), Now.AddMinutes(27));
        s.Approvals.Items[approval.Id] = approval;

        var message = await s.Reception.CancelQueueEntryAsync(entry.Id, CancellationToken.None);

        Assert.Equal("Queue entry cancelled.", message);
        Assert.Equal(QueueEntryStatus.Cancelled, entry.Status);
        Assert.Equal(WalkInAdmissionDecision.Expired, approval.Decision);
        Assert.Equal(Now, approval.DecisionAtUtc);
        Assert.Null(approval.UsedAtUtc);
    }

    [Fact]
    public async Task Cancelling_a_walk_in_leaves_an_already_approved_request_untouched()
    {
        var s = Fixture.Create();
        var entry = QueueEntry.CreateWalkIn(s.PatientId, s.DoctorId, Date, Now.AddMinutes(-3), 1);
        s.Queue.Add(entry);
        var approval = WalkInAdmissionApproval.Request(entry, Now.AddMinutes(-3), Now.AddMinutes(27));
        approval.Approve(Now.AddMinutes(-2));
        s.Approvals.Items[approval.Id] = approval;

        await s.Reception.CancelQueueEntryAsync(entry.Id, CancellationToken.None);

        Assert.Equal(WalkInAdmissionDecision.Approved, approval.Decision);
    }

    private sealed class Fixture
    {
        private Fixture(AppointmentService appointmentService, ReceptionQueueService queueService, InMemoryAppointmentRepository appointments,
            InMemoryQueueRepository queue, InMemoryApprovalRepository approvals, InMemoryUnitOfWork unitOfWork, FixedClock clock, Guid doctorId, Guid patientId)
        {
            Booking = appointmentService;
            Reception = queueService;
            Appointments = appointments;
            Queue = queue;
            Approvals = approvals;
            UnitOfWork = unitOfWork;
            Clock = clock;
            DoctorId = doctorId;
            PatientId = patientId;
        }

        public AppointmentService Booking { get; }
        public ReceptionQueueService Reception { get; }
        public InMemoryAppointmentRepository Appointments { get; }
        public InMemoryQueueRepository Queue { get; }
        public InMemoryApprovalRepository Approvals { get; }
        public InMemoryUnitOfWork UnitOfWork { get; }
        public FixedClock Clock { get; }
        public Guid DoctorId { get; }
        public Guid PatientId { get; }

        public static Fixture Create()
        {
            var appointments = new InMemoryAppointmentRepository();
            var queue = new InMemoryQueueRepository();
            var approvals = new InMemoryApprovalRepository();
            var unitOfWork = new InMemoryUnitOfWork();
            var clock = new FixedClock(Now, Date);
            var doctor = ScheduleTestData.NewDoctor();
            appointments.AddDoctor(doctor);
            ScheduleTestData.AddWeeklyWindow(appointments, doctor.Id, Date, new TimeOnly(9, 0), new TimeOnly(11, 0));
            var schedule = new InMemoryDoctorScheduleRepository();
            var availability = new DoctorAvailabilityService(appointments, schedule, clock);
            var appointmentService = new AppointmentService(appointments, unitOfWork, clock, queue, availability);
            var queueService = new ReceptionQueueService(queue, appointments, approvals, new InMemoryQueueBoardRepository(appointments, queue),
                new NoopGracePeriodService(), unitOfWork, clock, availability, new NoopDisruptionService(),
                new DoctorCallWindowService(appointments, new InMemoryDoctorDisruptionRepository(), new InMemoryDoctorScheduleRepository()));
            return new Fixture(appointmentService, queueService, appointments, queue, approvals, unitOfWork, clock, doctor.Id, Guid.NewGuid());
        }

        public Appointment AddConfirmedAppointment(TimeOnly start)
        {
            var appointment = Appointment.Request(PatientId, DoctorId, Date, start, start.AddMinutes(20), Now.AddDays(-1));
            appointment.Confirm(Now.AddDays(-1));
            Appointments.Appointments[appointment.Id] = appointment;
            return appointment;
        }
    }
}
