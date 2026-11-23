using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// A patient's place in a doctor's clinic queue for a given clinic date.
/// </summary>
public sealed class QueueEntry : BaseEntity
{
    private QueueEntry()
    {
        // Required by EF Core.
    }

    public static QueueEntry CreateScheduled(
        Guid patientProfileId,
        Guid doctorProfileId,
        Guid appointmentId,
        DateOnly queueDate,
        DateTime arrivedAt)
    {
        if (appointmentId == Guid.Empty)
        {
            throw new DomainException("Scheduled queue entries require an appointment.");
        }

        return new QueueEntry
        {
            PatientProfileId = patientProfileId,
            DoctorProfileId = doctorProfileId,
            AppointmentId = appointmentId,
            QueueDate = queueDate,
            Source = QueueEntrySource.Scheduled,
            ArrivedAt = arrivedAt.ToUniversalTime(),
            Status = QueueEntryStatus.Waiting
        };
    }

    public static QueueEntry CreateWalkIn(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly queueDate,
        DateTime arrivedAt,
        long queueSequence)
    {
        if (queueSequence <= 0)
        {
            throw new DomainException("Walk-in queue sequence must be positive.");
        }

        return new QueueEntry
        {
            PatientProfileId = patientProfileId,
            DoctorProfileId = doctorProfileId,
            AppointmentId = null,
            QueueDate = queueDate,
            Source = QueueEntrySource.WalkIn,
            ArrivedAt = arrivedAt.ToUniversalTime(),
            QueueSequence = queueSequence,
            Status = QueueEntryStatus.Waiting
        };
    }

    public Guid PatientProfileId { get; private set; }

    public PatientProfile PatientProfile { get; private set; } = null!;

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public Guid? AppointmentId { get; private set; }

    public Appointment? Appointment { get; private set; }

    public DateOnly QueueDate { get; private set; }

    public QueueEntrySource Source { get; private set; }

    public DateTime ArrivedAt { get; private set; }

    public long? QueueSequence { get; private set; }

    public QueueEntryStatus Status { get; private set; }

    public DateTime? CalledAt { get; private set; }

    public DateTime? ConsultationStartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public void Call(DateTime calledAt)
    {
        if (Source == QueueEntrySource.WalkIn)
        {
            throw new DomainException("Walk-in entries require an approved admission before they can be called.");
        }

        CallCore(calledAt);
    }

    public void CallWalkIn(WalkInAdmissionApproval approval, DateTime calledAt, DateOnly currentClinicDate)
    {
        if (Source != QueueEntrySource.WalkIn)
        {
            throw new DomainException("Admission approvals can only call walk-in entries.");
        }

        EnsureStatus(QueueEntryStatus.Waiting);
        if (calledAt < ArrivedAt)
        {
            throw new DomainException("Call time cannot precede arrival.");
        }

        approval.MarkUsed(this, calledAt, currentClinicDate);
        CallCore(calledAt);
    }

    private void CallCore(DateTime calledAt)
    {
        EnsureStatus(QueueEntryStatus.Waiting);
        if (calledAt < ArrivedAt)
        {
            throw new DomainException("Call time cannot precede arrival.");
        }

        CalledAt = calledAt.ToUniversalTime();
        Status = QueueEntryStatus.Called;
        MarkUpdated();
    }

    public void ReturnToWaiting()
    {
        EnsureStatus(QueueEntryStatus.Called);
        Status = QueueEntryStatus.Waiting;
        MarkUpdated();
    }

    public void StartConsultation(DateTime startedAt)
    {
        EnsureStatus(QueueEntryStatus.Called);
        if (CalledAt is null || startedAt < CalledAt)
        {
            throw new DomainException("Consultation start cannot precede the call.");
        }

        ConsultationStartedAt = startedAt.ToUniversalTime();
        Status = QueueEntryStatus.InConsultation;
        MarkUpdated();
    }

    public void Complete(DateTime completedAt)
    {
        EnsureStatus(QueueEntryStatus.InConsultation);
        if (ConsultationStartedAt is null || completedAt <= ConsultationStartedAt)
        {
            throw new DomainException("Completion must follow consultation start.");
        }

        CompletedAt = completedAt.ToUniversalTime();
        Status = QueueEntryStatus.Completed;
        MarkUpdated();
    }

    public void Cancel()
    {
        EnsureStatus(QueueEntryStatus.Waiting);
        Status = QueueEntryStatus.Cancelled;
        MarkUpdated();
    }

    public void MarkNoShow()
    {
        if (Status is not (QueueEntryStatus.Waiting or QueueEntryStatus.Called))
        {
            throw new DomainException($"A queue entry in {Status} cannot be marked as a no-show.");
        }

        Status = QueueEntryStatus.NoShow;
        MarkUpdated();
    }

    private void EnsureStatus(QueueEntryStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"A queue entry in {Status} cannot transition through this operation.");
        }
    }
}
