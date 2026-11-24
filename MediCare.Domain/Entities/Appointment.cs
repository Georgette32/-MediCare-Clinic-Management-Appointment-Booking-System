using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

public sealed class Appointment
{
    private Appointment() { }

    public Guid Id { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid? RescheduledFromAppointmentId { get; private set; }
    public DateOnly AppointmentDate { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public int? FrozenGracePeriodMinutes { get; private set; }
    public DateTimeOffset? GracePeriodStartedAtUtc { get; private set; }

    public Patient Patient { get; private set; } = null!;
    public Doctor Doctor { get; private set; } = null!;
    public Appointment? RescheduledFromAppointment { get; private set; }

    public static Appointment Request(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, DateTimeOffset createdAtUtc, Guid? rescheduledFromAppointmentId = null)
    {
        if (end <= start)
            throw new ArgumentException("Appointment end must be later than its start.", nameof(end));

        return new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            DoctorId = doctorId,
            RescheduledFromAppointmentId = rescheduledFromAppointmentId,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            Status = AppointmentStatus.Pending,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public void Confirm(DateTimeOffset updatedAtUtc)
    {
        EnsurePending();
        Status = AppointmentStatus.Confirmed;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void Reject(DateTimeOffset updatedAtUtc)
    {
        EnsurePending();
        Status = AppointmentStatus.Rejected;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void CancelByPatient(DateOnly clinicDate, DateTimeOffset updatedAtUtc, string? reason = null)
    {
        if (clinicDate >= AppointmentDate)
            throw new InvalidOperationException("Patients may cancel only before the appointment day.");
        if (Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            throw new InvalidOperationException("Only pending or confirmed appointments can be cancelled.");

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void CancelByClinic(string reason, DateTimeOffset updatedAtUtc)
    {
        if (Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            throw new InvalidOperationException("Only pending or confirmed appointments can be cancelled.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Cancels a Pending replacement whose original appointment was cancelled. The original's day rule has already
    /// been applied by the caller, so this only enforces that the replacement is still Pending.
    /// </summary>
    public void CancelAsOriginalCancelled(DateTimeOffset updatedAtUtc)
    {
        if (Status != AppointmentStatus.Pending)
            throw new InvalidOperationException("Only a pending replacement is cancelled with its original appointment.");

        Status = AppointmentStatus.Cancelled;
        CancellationReason = AppointmentCancellationReasons.OriginalCancelled;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public Appointment CreateRescheduledReplacement(Guid doctorId, DateOnly newDate, TimeOnly newStart, TimeOnly newEnd, DateTimeOffset createdAtUtc)
    {
        EnsureConfirmedForReplacement();
        return Request(PatientId, doctorId, newDate, newStart, newEnd, createdAtUtc, Id);
    }

    public Appointment ReplaceByClinic(Guid doctorId, DateOnly newDate, TimeOnly newStart, TimeOnly newEnd, DateTimeOffset updatedAtUtc)
    {
        EnsureConfirmedForReplacement();
        var replacement = Request(PatientId, doctorId, newDate, newStart, newEnd, updatedAtUtc, Id);
        replacement.Confirm(updatedAtUtc);
        Status = AppointmentStatus.Rescheduled;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        return replacement;
    }

    public void MarkRescheduled(DateTimeOffset updatedAtUtc)
    {
        EnsureConfirmedForReplacement();
        Status = AppointmentStatus.Rescheduled;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void StartGracePeriod(int gracePeriodMinutes, DateTimeOffset startedAtUtc)
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException("Grace period can be started only for confirmed appointments.");
        if (gracePeriodMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(gracePeriodMinutes), "Grace period minutes must be positive.");
        if (FrozenGracePeriodMinutes is not null)
            throw new InvalidOperationException("Grace period has already been started and frozen for this appointment.");

        FrozenGracePeriodMinutes = gracePeriodMinutes;
        GracePeriodStartedAtUtc = startedAtUtc.ToUniversalTime();
        UpdatedAtUtc = startedAtUtc.ToUniversalTime();
    }

    public bool IsGracePeriodExpired(DateTimeOffset currentUtc)
    {
        if (GracePeriodStartedAtUtc is null || FrozenGracePeriodMinutes is null)
            return false;

        return currentUtc >= GracePeriodStartedAtUtc.Value.AddMinutes(FrozenGracePeriodMinutes.Value);
    }

    public void MarkCompleted(DateTimeOffset updatedAtUtc)
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException("Only confirmed appointments can be marked Completed.");

        Status = AppointmentStatus.Completed;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void MarkNoShow(DateTimeOffset updatedAtUtc)
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException("Only confirmed appointments can be marked No-Show.");

        Status = AppointmentStatus.NoShow;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    private void EnsureConfirmedForReplacement()
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException("Only confirmed appointments can be replaced by rescheduling.");
    }

    private void EnsurePending()
    {
        if (Status != AppointmentStatus.Pending)
            throw new InvalidOperationException("Only pending appointments can be confirmed or rejected.");
    }
}
