using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// The single validation path that admits a Waiting walk-in with a consumed approval. Used by the explicit
/// walk-in Call operation and by Call Next so the two can never disagree. Performs no I/O: callers load the
/// context inside the same transaction and then call <see cref="ValidateAndCall"/>, which throws
/// InvalidOperationException on any rule violation and mutates the entry only on success.
/// </summary>
public static class WalkInCallRules
{
    public static void EnsureWaitingWalkIn(QueueEntry entry)
    {
        if (entry.Source != QueueEntrySource.WalkIn)
            throw new InvalidOperationException("Only walk-in queue entries can request or use walk-in admission approval.");
        if (entry.Status != QueueEntryStatus.Waiting)
            throw new InvalidOperationException("Only a Waiting walk-in entry can be admitted.");
    }

    public static void ValidateAndCall(QueueEntry entry, WalkInAdmissionApproval approval, WalkInCallContext context)
    {
        EnsureWaitingWalkIn(entry);
        if (approval.QueueEntryId != entry.Id || approval.DoctorId != entry.DoctorId)
            throw new InvalidOperationException("The approval belongs to a different queue entry or doctor.");

        if (approval.Decision != WalkInAdmissionDecision.Approved)
            throw new InvalidOperationException(approval.Decision == WalkInAdmissionDecision.Expired
                ? "This admission approval has expired."
                : "A current approved admission request is required before calling a walk-in.");
        if (approval.IsExpired(context.NowUtc, context.Today, entry.QueueDate))
            throw new InvalidOperationException("This admission approval has expired.");
        if (entry.QueueDate != context.Today)
            throw new InvalidOperationException("Only today's walk-in entries can be called.");
        if (approval.UsedAtUtc is not null)
            throw new InvalidOperationException("This admission approval has already been used.");

        if (!context.DoctorActive)
            throw new InvalidOperationException("The doctor is not active.");

        if (context.DoctorCallBlockReason is not null)
            throw new InvalidOperationException(context.DoctorCallBlockReason);

        if (context.ActiveEntries.Any(other => other.Id != entry.Id && other.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation))
            throw new InvalidOperationException("The doctor already has a patient Called or InConsultation.");

        if (context.ActiveEntries.Any(other => other.Id != entry.Id && other.Source == QueueEntrySource.Scheduled && other.Status == QueueEntryStatus.Waiting))
            throw new InvalidOperationException("A scheduled patient has checked in and is waiting; call that patient before a walk-in.");

        // FIFO among approved walk-ins only: an earlier walk-in without a usable approval does not block this one.
        if (context.ActiveEntries.Any(other => other.Id != entry.Id && other.Source == QueueEntrySource.WalkIn &&
                other.Status == QueueEntryStatus.Waiting && IsBeforeInFifo(other, entry) &&
                HoldsUsableApproval(other, context)))
            throw new InvalidOperationException("An earlier approved walk-in is ahead in FIFO order; call that patient first.");

        if (!context.WorkingHours.Any(hours => hours.StartTime <= context.CurrentTime && context.CurrentTime < hours.EndTime))
            throw new InvalidOperationException("The doctor is outside configured working hours.");

        entry.CallWalkIn(approval, context.NowUtc, context.Today);
    }

    /// <summary>
    /// True when the approval can be consumed now: Approved, unused, and not expired for the entry's clinic day.
    /// Call Next and the explicit Call both use this to decide which walk-in is admissible.
    /// </summary>
    public static bool IsUsableApproval(WalkInAdmissionApproval approval, QueueEntry entry, DateTimeOffset nowUtc, DateOnly today) =>
        approval.QueueEntryId == entry.Id &&
        approval.Decision == WalkInAdmissionDecision.Approved &&
        approval.UsedAtUtc is null &&
        !approval.IsExpired(nowUtc, today, entry.QueueDate);

    public static bool IsBeforeInFifo(QueueEntry candidate, QueueEntry entry) =>
        candidate.ArrivedAt < entry.ArrivedAt ||
        candidate.ArrivedAt == entry.ArrivedAt && candidate.QueueSequence < entry.QueueSequence;

    private static bool HoldsUsableApproval(QueueEntry entry, WalkInCallContext context) =>
        (context.WaitingApprovals ?? []).Any(approval => IsUsableApproval(approval, entry, context.NowUtc, context.Today));
}

/// <summary>
/// Inputs the walk-in call rules need, gathered inside the doctor-queue transaction. WorkingHours are the effective
/// windows from <see cref="IDoctorAvailabilityService"/>, so a time inside an approved extension is callable.
/// WaitingApprovals are the admission records of the active walk-ins, used for the approved-FIFO rule.
/// DoctorCallBlockReason is set when the doctor has left early or is on a dated break (see <see cref="IDoctorCallWindowService"/>).
/// </summary>
public sealed record WalkInCallContext(
    DateOnly Today,
    DateTimeOffset NowUtc,
    TimeOnly CurrentTime,
    bool DoctorActive,
    IReadOnlyList<QueueEntry> ActiveEntries,
    IReadOnlyList<EffectiveWorkingWindow> WorkingHours,
    IReadOnlyList<WalkInAdmissionApproval>? WaitingApprovals = null,
    string? DoctorCallBlockReason = null);
