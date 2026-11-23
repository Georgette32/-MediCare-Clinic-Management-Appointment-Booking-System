using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

public sealed class QueueEntry
{
    private QueueEntry() { }

    public static QueueEntry CreateScheduled(Guid patientId, Guid doctorId, Guid appointmentId, DateOnly queueDate, DateTimeOffset arrivedAt)
    {
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("Scheduled queue entries require an appointment.", nameof(appointmentId));

        return new QueueEntry
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            DoctorId = doctorId,
            AppointmentId = appointmentId,
            QueueDate = queueDate,
            Source = QueueEntrySource.Scheduled,
            ArrivedAt = arrivedAt.ToUniversalTime(),
            Status = QueueEntryStatus.Waiting,
            CreatedAt = arrivedAt.ToUniversalTime()
        };
    }

    public static QueueEntry CreateWalkIn(Guid patientId, Guid doctorId, DateOnly queueDate, DateTimeOffset arrivedAt, long queueSequence)
    {
        if (queueSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueSequence), "Walk-in queue sequence must be positive.");

        return new QueueEntry
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            DoctorId = doctorId,
            AppointmentId = null,
            QueueDate = queueDate,
            Source = QueueEntrySource.WalkIn,
            ArrivedAt = arrivedAt.ToUniversalTime(),
            QueueSequence = queueSequence,
            Status = QueueEntryStatus.Waiting,
            CreatedAt = arrivedAt.ToUniversalTime()
        };
    }

    public Guid Id { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public DateOnly QueueDate { get; private set; }
    public QueueEntrySource Source { get; private set; }
    public DateTimeOffset ArrivedAt { get; private set; }
    public long? QueueSequence { get; private set; }
    public QueueEntryStatus Status { get; private set; }
    public DateTimeOffset? CalledAt { get; private set; }
    public DateTimeOffset? ConsultationStartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Patient Patient { get; private set; } = null!;
    public Doctor Doctor { get; private set; } = null!;
    public Appointment? Appointment { get; private set; }

    public void Call(DateTimeOffset calledAt)
    {
        if (Source == QueueEntrySource.WalkIn)
            throw new InvalidOperationException("Walk-in entries require a valid admission approval and the Call operation.");

        CallCore(calledAt);
    }

    public void CallWalkIn(WalkInAdmissionApproval approval, DateTimeOffset calledAt, DateOnly currentClinicDate)
    {
        if (Source != QueueEntrySource.WalkIn)
            throw new InvalidOperationException("Admission approvals can only call walk-in entries.");

        EnsureStatus(QueueEntryStatus.Waiting);
        if (calledAt < ArrivedAt)
            throw new ArgumentOutOfRangeException(nameof(calledAt), "Call time cannot precede arrival.");
        approval.MarkUsed(this, calledAt, currentClinicDate);
        CallCore(calledAt);
    }

    private void CallCore(DateTimeOffset calledAt)
    {
        EnsureStatus(QueueEntryStatus.Waiting);
        if (calledAt < ArrivedAt)
            throw new ArgumentOutOfRangeException(nameof(calledAt), "Call time cannot precede arrival.");

        CalledAt = calledAt.ToUniversalTime();
        Status = QueueEntryStatus.Called;
    }

    public void ReturnToWaiting()
    {
        EnsureStatus(QueueEntryStatus.Called);
        Status = QueueEntryStatus.Waiting;
    }

    public void StartConsultation(DateTimeOffset startedAt)
    {
        EnsureStatus(QueueEntryStatus.Called);
        if (CalledAt is null || startedAt < CalledAt)
            throw new ArgumentOutOfRangeException(nameof(startedAt), "Consultation start cannot precede the call.");

        ConsultationStartedAt = startedAt.ToUniversalTime();
        Status = QueueEntryStatus.InConsultation;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureStatus(QueueEntryStatus.InConsultation);
        if (ConsultationStartedAt is null || completedAt <= ConsultationStartedAt)
            throw new ArgumentOutOfRangeException(nameof(completedAt), "Completion must follow consultation start.");

        CompletedAt = completedAt.ToUniversalTime();
        Status = QueueEntryStatus.Completed;
    }

    public void Cancel()
    {
        EnsureStatus(QueueEntryStatus.Waiting);
        Status = QueueEntryStatus.Cancelled;
    }

    public void MarkNoShow()
    {
        if (Status is not (QueueEntryStatus.Waiting or QueueEntryStatus.Called))
            throw new InvalidOperationException($"A queue entry in {Status} cannot be marked No-Show.");

        Status = QueueEntryStatus.NoShow;
    }

    private void EnsureStatus(QueueEntryStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"A queue entry in {Status} cannot transition through this operation.");
    }
}
