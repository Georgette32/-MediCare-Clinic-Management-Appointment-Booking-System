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
        var approval = ApprovedWalkInApproval(entry);
        entry.CallWalkIn(approval, ArrivedAt.AddMinutes(1), DateOnly.FromDateTime(ArrivedAt.UtcDateTime));
        entry.StartConsultation(ArrivedAt.AddMinutes(2));
        entry.Complete(ArrivedAt.AddMinutes(12));

        Assert.Equal(QueueEntryStatus.Completed, entry.Status);
        Assert.Equal(ArrivedAt.AddMinutes(2), entry.ConsultationStartedAt);
        Assert.Equal(ArrivedAt.AddMinutes(12), entry.CompletedAt);
    }

    [Fact]
    public void Cancel_records_when_the_patient_left()
    {
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt);
        var leftAt = ArrivedAt.AddMinutes(37);

        entry.Cancel(leftAt);

        Assert.Equal(QueueEntryStatus.Cancelled, entry.Status);
        Assert.Equal(leftAt, entry.ClosedAtUtc);
    }

    [Fact]
    public void Cancel_stores_the_close_time_in_utc()
    {
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt);
        var leftAt = new DateTimeOffset(2026, 11, 21, 12, 30, 0, TimeSpan.FromHours(3));

        entry.Cancel(leftAt);

        Assert.Equal(TimeSpan.Zero, entry.ClosedAtUtc!.Value.Offset);
        Assert.Equal(leftAt.UtcDateTime, entry.ClosedAtUtc.Value.UtcDateTime);
    }

    [Fact]
    public void Cancel_refuses_a_close_time_before_arrival()
    {
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => entry.Cancel(ArrivedAt.AddSeconds(-1)));
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
        Assert.Null(entry.ClosedAtUtc);
    }

    [Fact]
    public void Cancel_as_stale_leaves_the_close_time_empty()
    {
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt);

        entry.CancelAsStale();

        Assert.Equal(QueueEntryStatus.Cancelled, entry.Status);
        Assert.Null(entry.ClosedAtUtc);
    }

    [Fact]
    public void Queue_entry_rejects_invalid_timestamp_order()
    {
        var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, 1);

        var approval = ApprovedWalkInApproval(entry);
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.CallWalkIn(approval, ArrivedAt.AddSeconds(-1), DateOnly.FromDateTime(ArrivedAt.UtcDateTime)));
        entry.CallWalkIn(approval, ArrivedAt.AddMinutes(1), DateOnly.FromDateTime(ArrivedAt.UtcDateTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.StartConsultation(ArrivedAt));
    }

    [Fact]
    public void Walk_in_cannot_be_called_through_the_generic_transition()
    {
        var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, 1);

        Assert.Throws<InvalidOperationException>(() => entry.Call(ArrivedAt.AddMinutes(1)));
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public void Approval_for_one_queue_entry_cannot_be_used_to_call_another_entry()
    {
        var doctorId = Guid.NewGuid();
        var first = QueueEntry.CreateWalkIn(Guid.NewGuid(), doctorId, DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt, 1);
        var second = QueueEntry.CreateWalkIn(Guid.NewGuid(), doctorId, DateOnly.FromDateTime(ArrivedAt.UtcDateTime), ArrivedAt.AddMinutes(1), 2);
        var approval = ApprovedWalkInApproval(first);

        Assert.Throws<InvalidOperationException>(() => second.CallWalkIn(approval, ArrivedAt.AddMinutes(2), DateOnly.FromDateTime(ArrivedAt.UtcDateTime)));

        Assert.Equal(QueueEntryStatus.Waiting, first.Status);
        Assert.Equal(QueueEntryStatus.Waiting, second.Status);
        Assert.Null(approval.UsedAtUtc);
    }

    private static WalkInAdmissionApproval ApprovedWalkInApproval(QueueEntry entry)
    {
        var requestedAt = entry.ArrivedAt.AddMinutes(-1);
        var approval = WalkInAdmissionApproval.Request(entry, requestedAt, requestedAt.AddHours(1));
        approval.Approve(entry.ArrivedAt);
        return approval;
    }
}
