using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class QueueLifecycleDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mark_completed_from_confirmed_sets_completed_and_records_the_update_time()
    {
        var appointment = ConfirmedAppointment();

        appointment.MarkCompleted(Now.AddMinutes(20));

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(Now.AddMinutes(20), appointment.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Rescheduled)]
    public void Mark_completed_is_allowed_only_from_confirmed(AppointmentStatus from)
    {
        var appointment = Appointment.Request(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(9, 0), new TimeOnly(9, 20), Now);
        SetStatus(appointment, from);

        var exception = Assert.Throws<InvalidOperationException>(() => appointment.MarkCompleted(Now));

        Assert.Contains("Only confirmed", exception.Message);
        Assert.Equal(from, appointment.Status);
    }

    [Theory]
    [InlineData(QueueEntryStatus.Waiting, true)]
    [InlineData(QueueEntryStatus.Called, true)]
    [InlineData(QueueEntryStatus.InConsultation, true)]
    [InlineData(QueueEntryStatus.Completed, false)]
    [InlineData(QueueEntryStatus.Cancelled, false)]
    [InlineData(QueueEntryStatus.NoShow, false)]
    public void Only_waiting_called_and_in_consultation_attempts_are_live(QueueEntryStatus status, bool expectedLive)
    {
        Assert.Equal(expectedLive, status.IsLive());
    }

    private static Appointment ConfirmedAppointment()
    {
        var appointment = Appointment.Request(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(9, 0), new TimeOnly(9, 20), Now);
        appointment.Confirm(Now);
        return appointment;
    }

    private static void SetStatus(Appointment appointment, AppointmentStatus status)
    {
        var field = typeof(Appointment).GetProperty(nameof(Appointment.Status))!;
        field.SetValue(appointment, status);
    }
}
