using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class AppointmentDomainTests
{
    private static readonly DateTimeOffset UpdatedAtUtc = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Doctor_requires_configured_positive_appointment_duration()
    {
        var specializationId = Guid.NewGuid();
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Smith", specializationId, 25);

        Assert.Equal(25, doctor.AppointmentDurationMinutes);
        Assert.Equal(specializationId, doctor.SpecializationId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Smith", specializationId, 0));
    }

    [Fact]
    public void Patient_profile_requires_account_link()
    {
        var accountId = Guid.NewGuid();
        var patient = new Patient(Guid.NewGuid(), accountId, "Patient One", "01000000000");

        Assert.Equal(accountId, patient.ApplicationUserId);
    }

    [Fact]
    public void Patient_can_cancel_before_appointment_day()
    {
        var appointment = CreateAppointment(new DateOnly(2026, 12, 1));

        appointment.CancelByPatient(new DateOnly(2026, 11, 30), UpdatedAtUtc, "Schedule changed");

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("Schedule changed", appointment.CancellationReason);
    }

    [Fact]
    public void Patient_cannot_cancel_on_appointment_day()
    {
        var appointment = CreateAppointment(new DateOnly(2026, 12, 1));

        Assert.Throws<InvalidOperationException>(() => appointment.CancelByPatient(new DateOnly(2026, 12, 1), UpdatedAtUtc));
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
    }

    [Fact]
    public void Patient_cannot_cancel_after_appointment_day()
    {
        var appointment = CreateAppointment(new DateOnly(2026, 12, 1));

        Assert.Throws<InvalidOperationException>(() => appointment.CancelByPatient(new DateOnly(2026, 12, 2), UpdatedAtUtc));
    }

    [Fact]
    public void Rescheduling_preserves_old_appointment_and_links_new_pending_appointment()
    {
        var original = CreateConfirmedAppointment(new DateOnly(2026, 12, 1));

        var replacementDoctorId = Guid.NewGuid();
        var replacement = original.CreateRescheduledReplacement(replacementDoctorId, new DateOnly(2026, 12, 3), new TimeOnly(10, 0), new TimeOnly(10, 25), UpdatedAtUtc);

        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
        Assert.Equal(original.Id, replacement.RescheduledFromAppointmentId);
        Assert.Equal(AppointmentStatus.Pending, replacement.Status);
        Assert.Equal(original.PatientId, replacement.PatientId);
        Assert.Equal(replacementDoctorId, replacement.DoctorId);

        replacement.Confirm(UpdatedAtUtc);
        original.MarkRescheduled(UpdatedAtUtc);

        Assert.Equal(AppointmentStatus.Confirmed, replacement.Status);
        Assert.Equal(AppointmentStatus.Rescheduled, original.Status);
    }

    [Fact]
    public void Rejected_appointment_cannot_be_rescheduled()
    {
        var appointment = CreateAppointment(new DateOnly(2026, 12, 1));
        appointment.Reject(UpdatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => appointment.CreateRescheduledReplacement(appointment.DoctorId, new DateOnly(2026, 12, 3), new TimeOnly(10, 0), new TimeOnly(10, 25), UpdatedAtUtc));
    }

    private static Appointment CreateAppointment(DateOnly date) =>
        Appointment.Request(Guid.NewGuid(), Guid.NewGuid(), date, new TimeOnly(10, 0), new TimeOnly(10, 25), UpdatedAtUtc);

    private static Appointment CreateConfirmedAppointment(DateOnly date)
    {
        var appointment = CreateAppointment(date);
        appointment.Confirm(UpdatedAtUtc);
        return appointment;
    }
}
