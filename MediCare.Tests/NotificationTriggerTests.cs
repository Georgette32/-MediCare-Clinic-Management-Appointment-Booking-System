using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

/// <summary>
/// Each business change that should notify someone does so once, after it commits; a failing provider never fails the
/// change itself.
/// </summary>
public sealed class NotificationTriggerTests
{
    private static readonly DateOnly Today = QueueTestData.Today;
    private static readonly DateOnly AppointmentDay = Today.AddDays(4);

    [Fact]
    public async Task Confirming_a_booking_request_notifies_the_patient_once()
    {
        var w = new World();
        var id = w.AddPending(10, 0);

        await w.Service.ConfirmAsync(id, CancellationToken.None);

        var notice = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.BookingRequestConfirmed, notice.Kind);
        Assert.Equal(w.Patient.Id, notice.PatientId);
        Assert.Equal(id, notice.RelatedAppointmentId);
        Assert.Contains(AppointmentDay.ToString("yyyy-MM-dd"), notice.Message);
    }

    [Fact]
    public async Task Rejecting_a_booking_request_notifies_the_patient()
    {
        var w = new World();
        var id = w.AddPending(10, 0);

        await w.Service.RejectAsync(id, CancellationToken.None);

        var notice = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.BookingRequestRejected, notice.Kind);
        Assert.Equal(w.Patient.Id, notice.PatientId);
    }

    [Fact]
    public async Task Confirming_a_patient_reschedule_notifies_the_patient_and_marks_the_original_rescheduled()
    {
        var w = new World();
        var original = w.AddConfirmed(10, 0);
        var replacement = original.CreateRescheduledReplacement(w.Doctor.Id, AppointmentDay.AddDays(1), new TimeOnly(11, 0), new TimeOnly(11, 20), w.Clock.UtcNow);
        w.Appointments.Appointments[replacement.Id] = replacement;

        await w.Service.ConfirmAsync(replacement.Id, CancellationToken.None);

        var notice = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.RescheduleRequestConfirmed, notice.Kind);
        Assert.Equal(AppointmentStatus.Rescheduled, original.Status);
    }

    [Fact]
    public async Task Rejecting_a_patient_reschedule_tells_the_patient_the_original_stands()
    {
        var w = new World();
        var original = w.AddConfirmed(10, 0);
        var replacement = original.CreateRescheduledReplacement(w.Doctor.Id, AppointmentDay.AddDays(1), new TimeOnly(11, 0), new TimeOnly(11, 20), w.Clock.UtcNow);
        w.Appointments.Appointments[replacement.Id] = replacement;

        await w.Service.RejectAsync(replacement.Id, CancellationToken.None);

        var notice = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.RescheduleRequestRejected, notice.Kind);
        Assert.Contains("original appointment stays", notice.Message);
        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
    }

    [Fact]
    public async Task Clinic_cancellation_notifies_the_patient_with_the_reason()
    {
        var w = new World();
        var id = w.AddConfirmed(10, 0).Id;

        await w.Service.CancelByClinicAsync(id, ClinicCancellationActor.Reception, "Doctor called in sick", CancellationToken.None);

        var notice = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.AppointmentCancelledByClinic, notice.Kind);
        Assert.Contains("Doctor called in sick", notice.Message);
        Assert.Equal(id, notice.RelatedAppointmentId);
    }

    [Fact]
    public async Task Clinic_rescheduling_notifies_the_patient_with_the_new_date_and_time()
    {
        var w = new World();
        var id = w.AddConfirmed(10, 0).Id;
        var newDate = AppointmentDay.AddDays(2);

        await w.Service.RescheduleByClinicAsync(id, ClinicCancellationActor.Reception, w.Doctor.Id, newDate, new TimeOnly(11, 0), CancellationToken.None);

        var notice = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.AppointmentRescheduledByClinic, notice.Kind);
        Assert.Contains(newDate.ToString("yyyy-MM-dd"), notice.Message);
        Assert.Contains("11:00", notice.Message);
    }

    [Fact]
    public async Task A_failing_provider_does_not_fail_the_confirmation()
    {
        var w = new World();
        var id = w.AddPending(10, 0);
        var provider = new FailingNotificationProvider();
        var notifications = new InMemoryNotificationRepository();
        notifications.PatientUsers[w.Patient.Id] = Guid.NewGuid();
        var service = new AppointmentService(w.Appointments, w.UnitOfWork, w.Clock, w.Queue, w.Availability,
            new SafePatientNotificationPublisher(
                new NotificationService(notifications, provider, w.UnitOfWork, w.Clock),
                NullLogger<SafePatientNotificationPublisher>.Instance));

        await service.ConfirmAsync(id, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Confirmed, w.Appointments.Appointments[id].Status);
        Assert.Equal(1, provider.Attempts);
    }

    [Fact]
    public async Task A_walk_in_admission_request_notifies_the_doctor_without_naming_the_patient()
    {
        var w = new World();
        var entry = QueueEntry.CreateWalkIn(w.Patient.Id, w.Doctor.Id, Today, w.Clock.UtcNow, 3);
        w.Queue.Add(entry);
        var service = new WalkInAdmissionApprovalService(w.Queue, w.Appointments, new InMemoryApprovalRepository(), w.UnitOfWork, w.Clock,
            Options.Create(new WalkInAdmissionApprovalOptions { PendingDurationMinutes = 30 }), w.Availability,
            new DoctorCallWindowService(w.Appointments, new InMemoryDoctorDisruptionRepository(), w.Schedule), w.Staff);

        var approvalId = await service.RequestAsync(entry.Id, CancellationToken.None);

        var notice = Assert.Single(w.Staff.ToDoctors);
        Assert.Equal(w.Doctor.Id, notice.Id);
        Assert.Equal(StaffNotificationKind.WalkInAdmissionRequested, notice.Kind);
        Assert.Equal(approvalId, notice.RelatedId);
        Assert.DoesNotContain(w.Patient.FullName, notice.Message);
        Assert.Contains("3", notice.Message);
    }

    [Fact]
    public async Task A_working_hour_extension_request_notifies_the_doctor()
    {
        var w = new World();
        var service = new WorkingHourExtensionService(w.Appointments, w.Schedule, w.UnitOfWork, w.Clock, w.Staff);

        var result = await service.RequestAsync(w.Doctor.Id, AppointmentDay, new TimeOnly(17, 0), new TimeOnly(18, 0), "Clinic overflow", Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var notice = Assert.Single(w.Staff.ToDoctors);
        Assert.Equal(w.Doctor.Id, notice.Id);
        Assert.Equal(StaffNotificationKind.WorkingHourExtensionRequested, notice.Kind);
        Assert.Equal(result.ExtensionId, notice.RelatedId);
    }

    [Theory]
    [InlineData(true, "approved")]
    [InlineData(false, "rejected")]
    public async Task The_extension_decision_notifies_the_reception_user_who_requested_it(bool approve, string outcome)
    {
        var w = new World();
        var requester = Guid.NewGuid();
        var service = new WorkingHourExtensionService(w.Appointments, w.Schedule, w.UnitOfWork, w.Clock, w.Staff);
        var requested = await service.RequestAsync(w.Doctor.Id, AppointmentDay, new TimeOnly(17, 0), new TimeOnly(18, 0), "Clinic overflow", requester, CancellationToken.None);
        w.Staff.ToDoctors.Clear();

        var decision = await service.DecideAsync(requested.ExtensionId!.Value, w.DoctorUserId, approve, null, CancellationToken.None);

        Assert.True(decision.Succeeded, decision.Message);
        var notice = Assert.Single(w.Staff.ToUsers);
        Assert.Equal(requester, notice.Id);
        Assert.Equal(StaffNotificationKind.WorkingHourExtensionDecided, notice.Kind);
        Assert.Contains(outcome, notice.Message);
        Assert.Empty(w.Staff.ToDoctors);
    }

    private sealed class World
    {
        public World()
        {
            DoctorUserId = Guid.NewGuid();
            Doctor = QueueTestData.NewDoctor(DoctorUserId);
            Appointments.AddDoctor(Doctor);
            foreach (var day in Enum.GetValues<DayOfWeek>())
                Appointments.WorkingHours.Add(DoctorWorkingHour.Create(Doctor.Id, day, new TimeOnly(9, 0), new TimeOnly(17, 0)));
            Patient = QueueTestData.NewPatient("Test Patient");
            Clock = new FixedClock(new DateTimeOffset(Today.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero), Today);
            Availability = new DoctorAvailabilityService(Appointments, Schedule, Clock);
            Service = new AppointmentService(Appointments, UnitOfWork, Clock, Queue, Availability, Publisher);
        }

        public Guid DoctorUserId { get; }
        public Doctor Doctor { get; }
        public Patient Patient { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public InMemoryDoctorScheduleRepository Schedule { get; } = new();
        public InMemoryUnitOfWork UnitOfWork { get; } = new();
        public FixedClock Clock { get; }
        public DoctorAvailabilityService Availability { get; }
        public RecordingPatientNotificationPublisher Publisher { get; } = new();
        public RecordingStaffNotificationPublisher Staff { get; } = new();
        public AppointmentService Service { get; }

        public Guid AddPending(int hour, int minute) => Add(hour, minute, confirmed: false).Id;

        public Appointment AddConfirmed(int hour, int minute) => Add(hour, minute, confirmed: true);

        private Appointment Add(int hour, int minute, bool confirmed)
        {
            var now = Clock.UtcNow;
            var appointment = Appointment.Request(Patient.Id, Doctor.Id, AppointmentDay, new TimeOnly(hour, minute), new TimeOnly(hour, minute).AddMinutes(20), now);
            if (confirmed)
                appointment.Confirm(now);
            Appointments.Appointments[appointment.Id] = appointment;
            return appointment;
        }
    }
}
