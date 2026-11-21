using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class QueueEntryDomainTests
{
    private static readonly DateTimeOffset ArrivedAt = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Walk_in_uses_sequence_and_has_no_appointment_link()
    {
        var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, 1);

        Assert.Equal(QueueEntrySource.WalkIn, entry.Source);
        Assert.Null(entry.AppointmentId);
        Assert.Equal(1, entry.QueueSequence);
    }

    [Fact]
    public void Scheduled_entry_has_appointment_link_and_no_queue_sequence()
    {
        var appointmentId = Guid.NewGuid();
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), appointmentId, DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt);

        Assert.Equal(QueueEntrySource.Scheduled, entry.Source);
        Assert.Equal(appointmentId, entry.AppointmentId);
        Assert.Null(entry.QueueSequence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Walk_in_sequence_must_be_positive(long sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QueueEntry.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, sequence));
    }

    [Fact]
    public void Queue_entry_enforces_valid_consultation_transitions_and_timestamps()
    {
        var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, 1);

        Assert.Throws<InvalidOperationException>(() => entry.StartConsultation(ArrivedAt.AddMinutes(2)));
        entry.Call(ArrivedAt.AddMinutes(1));
        entry.StartConsultation(ArrivedAt.AddMinutes(2));
        entry.Complete(ArrivedAt.AddMinutes(12));

        Assert.Equal(QueueEntryStatus.Completed, entry.Status);
        Assert.Equal(ArrivedAt.AddMinutes(2), entry.ConsultationStartedAt);
        Assert.Equal(ArrivedAt.AddMinutes(12), entry.CompletedAt);
    }

    [Fact]
    public void Queue_entry_rejects_invalid_timestamp_order()
    {
        var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => entry.Call(ArrivedAt.AddSeconds(-1)));
        entry.Call(ArrivedAt.AddMinutes(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.StartConsultation(ArrivedAt));
    }
}
