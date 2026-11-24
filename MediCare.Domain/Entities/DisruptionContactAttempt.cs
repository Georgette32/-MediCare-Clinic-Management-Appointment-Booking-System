using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

/// <summary>History row for a staff action on a worklist item: who acted, when, what was noted and the resulting outcome.</summary>
public sealed class DisruptionContactAttempt
{
    private DisruptionContactAttempt() { }

    public static DisruptionContactAttempt Create(Guid worklistItemId, Guid contactedByUserId, DisruptionOutcome outcome, string? notes, DateTimeOffset contactedAtUtc)
    {
        if (worklistItemId == Guid.Empty)
            throw new ArgumentException("A contact attempt requires a worklist item.", nameof(worklistItemId));
        if (contactedByUserId == Guid.Empty)
            throw new ArgumentException("A contact attempt requires the acting user.", nameof(contactedByUserId));

        var trimmed = notes?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > DisruptionWorklistItem.MaxNotesLength)
            throw new ArgumentException($"Notes cannot exceed {DisruptionWorklistItem.MaxNotesLength} characters.", nameof(notes));

        return new DisruptionContactAttempt
        {
            Id = Guid.NewGuid(),
            WorklistItemId = worklistItemId,
            ContactedByUserId = contactedByUserId,
            ContactedAtUtc = contactedAtUtc.ToUniversalTime(),
            Notes = string.IsNullOrEmpty(trimmed) ? null : trimmed,
            Outcome = outcome
        };
    }

    public Guid Id { get; private set; }
    public Guid WorklistItemId { get; private set; }
    public Guid ContactedByUserId { get; private set; }
    public DateTimeOffset ContactedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public DisruptionOutcome Outcome { get; private set; }
}
