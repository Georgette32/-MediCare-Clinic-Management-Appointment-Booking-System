using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Part E: a Reception/Admin booking notifies the patient after the booking commits.</summary>
public sealed class ClinicBookingNotificationTests
{
    [Fact]
    public async Task Clinic_booking_notifies_the_patient_after_commit()
    {
        var world = new BookingWorld();

        var appointmentId = await world.Service.BookByClinicAsync(
            world.PatientId, world.Doctor.Id, world.Tomorrow, new TimeOnly(10, 0), ClinicCancellationActor.Reception, CancellationToken.None);

        var appointment = world.Appointments.Appointments[appointmentId];
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(1, world.UnitOfWork.CommitCount);

        var notification = Assert.Single(world.Publisher.Published);
        Assert.Equal(world.PatientId, notification.PatientId);
        Assert.Equal(PatientNotificationKind.AppointmentBookedByClinic, notification.Kind);
        Assert.Equal(appointmentId, notification.RelatedAppointmentId);
        Assert.Contains("confirmed", notification.Message);
    }

    [Fact]
    public async Task A_refused_clinic_booking_publishes_no_notification()
    {
        var world = new BookingWorld();
        var existing = Appointment.Request(world.PatientId, world.Doctor.Id, world.Tomorrow, new TimeOnly(10, 0), new TimeOnly(10, 20), world.Now);
        existing.Confirm(world.Now);
        world.Appointments.Appointments.Add(existing.Id, existing);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            world.Service.BookByClinicAsync(world.PatientId, world.Doctor.Id, world.Tomorrow, new TimeOnly(10, 10), ClinicCancellationActor.Reception, CancellationToken.None));

        Assert.Empty(world.Publisher.Published);
    }

    private sealed class BookingWorld
    {
        public DateTimeOffset Now { get; } = QueueTestData.Noon;
        public DateOnly Tomorrow { get; } = QueueTestData.Today.AddDays(1);
        public Guid PatientId { get; } = Guid.NewGuid();
        public Doctor Doctor { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryUnitOfWork UnitOfWork { get; } = new();
        public RecordingPatientNotificationPublisher Publisher { get; } = new();
        public AppointmentService Service { get; }

        public BookingWorld()
        {
            Doctor = QueueTestData.NewDoctor();
            Appointments.AddDoctor(Doctor);
            foreach (var day in Enum.GetValues<DayOfWeek>())
                Appointments.WorkingHours.Add(DoctorWorkingHour.Create(Doctor.Id, day, new TimeOnly(8, 0), new TimeOnly(17, 0)));

            var clock = new FixedClock(Now, QueueTestData.Today);
            var availability = new DoctorAvailabilityService(Appointments, new InMemoryDoctorScheduleRepository(), clock);
            Service = new AppointmentService(Appointments, UnitOfWork, clock, new InMemoryQueueRepository(), availability, Publisher);
        }
    }
}
