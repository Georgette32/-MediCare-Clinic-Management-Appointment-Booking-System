using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

/// <summary>
/// One patient affected by a disruption: a Confirmed appointment and/or a Waiting queue entry. A scheduled patient who
/// is checked in is a single item linked to both. An item is open until it is resolved. Resolution is either an
/// outcome (Rescheduled or Cancelled, recorded with history or by the clinic-unavailable path) or an automatic
/// closure when the appointment or queue entry no longer needs staff action (for example it was completed or
/// the patient is already being served). Automatic closures keep their Outcome and carry <see cref="ResolutionNote"/>.
/// </summary>
public sealed class DisruptionWorklistItem
{
    public const int MaxNotesLength = 1000;
    public const int MaxResolutionNoteLength = 200;

    private DisruptionWorklistItem() { }

    public static DisruptionWorklistItem CreateForAppointment(Guid disruptionId, Guid patientId, Guid appointmentId, Guid? queueEntryId, DateTimeOffset createdAtUtc)
    {
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("An appointment id is required.", nameof(appointmentId));

        return new DisruptionWorklistItem
        {
            Id = Guid.NewGuid(),
            DisruptionId = disruptionId,
            PatientId = patientId,
            AppointmentId = appointmentId,
            QueueEntryId = queueEntryId,
            Outcome = DisruptionOutcome.Pending,
            UpdatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public static DisruptionWorklistItem CreateForQueueEntry(Guid disruptionId, Guid patientId, Guid queueEntryId, DateTimeOffset createdAtUtc)
    {
        if (queueEntryId == Guid.Empty)
            throw new ArgumentException("A queue entry id is required.", nameof(queueEntryId));

        return new DisruptionWorklistItem
        {
            Id = Guid.NewGuid(),
            DisruptionId = disruptionId,
            PatientId = patientId,
            AppointmentId = null,
            QueueEntryId = queueEntryId,
            Outcome = DisruptionOutcome.Pending,
            UpdatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public Guid Id { get; private set; }
    public Guid DisruptionId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? QueueEntryId { get; private set; }
    public DisruptionOutcome Outcome { get; private set; }
    public Guid? LastContactedByUserId { get; private set; }
    public DateTimeOffset? LastContactedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public string? ResolutionNote { get; private set; }

    public DoctorDisruption Disruption { get; private set; } = null!;
    public Patient Patient { get; private set; } = null!;
    public Appointment? Appointment { get; private set; }
    public QueueEntry? QueueEntry { get; private set; }

    /// <summary>Open until resolved by an outcome or closed automatically. The Outcome alone does not decide this.</summary>
    public bool IsOpen => ResolvedAtUtc is null;

    /// <summary>Staff reached the patient. The item stays open until it is rescheduled or cancelled.</summary>
    public DisruptionContactAttempt LogContact(Guid actorUserId, string? notes, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        var attempt = DisruptionContactAttempt.Create(Id, actorUserId, DisruptionOutcome.Contacted, notes, nowUtc);
        Outcome = DisruptionOutcome.Contacted;
        LastContactedByUserId = actorUserId;
        LastContactedAtUtc = nowUtc.ToUniversalTime();
        Notes = NormalizeNotes(notes) ?? Notes;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        return attempt;
    }

    /// <summary>Closes the item after a staff action (reschedule or clinic cancellation) and records it in history.</summary>
    public DisruptionContactAttempt Resolve(DisruptionOutcome outcome, Guid actorUserId, string? notes, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        EnsureClosingOutcome(outcome);
        var attempt = DisruptionContactAttempt.Create(Id, actorUserId, outcome, notes, nowUtc);
        Outcome = outcome;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        ResolvedAtUtc = nowUtc.ToUniversalTime();
        LastContactedByUserId = actorUserId;
        LastContactedAtUtc = nowUtc.ToUniversalTime();
        Notes = NormalizeNotes(notes) ?? Notes;
        return attempt;
    }

    /// <summary>
    /// Closes the item without a staff contact, when the system cancels it (clinic unavailable) or when the appointment
    /// already reflects a Rescheduled or Cancelled outcome. No history row is written for system actions.
    /// </summary>
    public void ResolveAutomatically(DisruptionOutcome outcome, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        EnsureClosingOutcome(outcome);
        Outcome = outcome;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        ResolvedAtUtc = nowUtc.ToUniversalTime();
    }

    /// <summary>
    /// Closes an item that no longer needs staff action while keeping its current outcome, for example when the
    /// appointment was completed, the patient is already being served, or the no-show was recorded. The note explains why.
    /// </summary>
    public void CloseAsNoLongerNeeded(string note, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("A closing note is required.", nameof(note));
        if (trimmed.Length > MaxResolutionNoteLength)
            throw new ArgumentException($"A closing note cannot exceed {MaxResolutionNoteLength} characters.", nameof(note));

        ResolutionNote = trimmed;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        ResolvedAtUtc = nowUtc.ToUniversalTime();
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("This worklist item is already resolved.");
    }

    private static void EnsureClosingOutcome(DisruptionOutcome outcome)
    {
        if (outcome is not (DisruptionOutcome.Rescheduled or DisruptionOutcome.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(outcome), "A worklist item can be closed only as Rescheduled or Cancelled.");
    }

    private static string? NormalizeNotes(string? notes)
    {
        var trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > MaxNotesLength)
            throw new ArgumentException($"Notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
        return trimmed;
    }
}
