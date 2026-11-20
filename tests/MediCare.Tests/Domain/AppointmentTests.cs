using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Domain;

public class AppointmentTests
{
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();

    private static Appointment CreateAppointment() =>
        new(PatientId, DoctorId, DateTime.UtcNow.AddHours(2), 50m, "  Follow-up  ");

    [Fact]
    public void NewAppointment_IsPendingAndNormalized()
    {
        var appointment = CreateAppointment();

        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal("Follow-up", appointment.Reason);
        Assert.Equal(50m, appointment.Fee);
    }

    [Fact]
    public void Constructor_RejectsMissingParticipants()
    {
        var future = DateTime.UtcNow.AddHours(1);

        Assert.Throws<DomainException>(() => new Appointment(Guid.Empty, DoctorId, future, 10m));
        Assert.Throws<DomainException>(() => new Appointment(PatientId, Guid.Empty, future, 10m));
    }

    [Fact]
    public void Constructor_RejectsPastSchedule()
    {
        Assert.Throws<DomainException>(() =>
            new Appointment(PatientId, DoctorId, DateTime.UtcNow.AddMinutes(-1), 10m));
    }

    [Fact]
    public void Constructor_RejectsNegativeFee()
    {
        Assert.Throws<DomainException>(() =>
            new Appointment(PatientId, DoctorId, DateTime.UtcNow.AddHours(1), -1m));
    }

    [Fact]
    public void Confirm_OnlyAllowedFromPending()
    {
        var appointment = CreateAppointment();

        appointment.Confirm();

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Throws<DomainException>(() => appointment.Confirm());
    }

    [Fact]
    public void Cancel_IsRejectedAfterCompletion()
    {
        var appointment = CreateAppointment();
        appointment.Confirm();
        appointment.Complete("All good.");

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal("All good.", appointment.DoctorNotes);
        Assert.Throws<DomainException>(() => appointment.Cancel());
    }

    [Fact]
    public void Complete_OnlyAllowedWhenConfirmed()
    {
        var appointment = CreateAppointment();

        Assert.Throws<DomainException>(() => appointment.Complete(null));
    }

    [Fact]
    public void MarkNoShow_OnlyAllowedWhenConfirmed()
    {
        var appointment = CreateAppointment();

        Assert.Throws<DomainException>(() => appointment.MarkNoShow());

        appointment.Confirm();
        appointment.MarkNoShow();

        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
    }
}
