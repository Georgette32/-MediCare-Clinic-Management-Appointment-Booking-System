using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

public sealed class WalkInAdmissionApproval
{
    private WalkInAdmissionApproval() { }

    public Guid Id { get; private set; }
    public Guid QueueEntryId { get; private set; }
    public Guid DoctorId { get; private set; }
    public WalkInAdmissionDecision Decision { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? DecisionAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    public QueueEntry QueueEntry { get; private set; } = null!;

    public static WalkInAdmissionApproval Request(QueueEntry queueEntry, DateTimeOffset requestedAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(queueEntry);
        if (queueEntry.Id == Guid.Empty)
            throw new ArgumentException("An admission approval must be associated with a queue entry.", nameof(queueEntry));
        if (queueEntry.DoctorId == Guid.Empty)
            throw new ArgumentException("An admission approval must be associated with a doctor.", nameof(queueEntry));
        if (queueEntry.Source != QueueEntrySource.WalkIn)
            throw new InvalidOperationException("Admission approvals can only be requested for walk-in entries.");
        if (expiresAtUtc <= requestedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Approval expiry must follow its request time.");

        return new WalkInAdmissionApproval
        {
            Id = Guid.NewGuid(),
            QueueEntryId = queueEntry.Id,
            DoctorId = queueEntry.DoctorId,
            QueueEntry = queueEntry,
            Decision = WalkInAdmissionDecision.Pending,
            RequestedAtUtc = requestedAtUtc.ToUniversalTime(),
            ExpiresAtUtc = expiresAtUtc.ToUniversalTime()
        };
    }

    public void Approve(DateTimeOffset decidedAtUtc, string? notes = null) => Decide(WalkInAdmissionDecision.Approved, decidedAtUtc, notes);

    public void Reject(DateTimeOffset decidedAtUtc, string? notes = null) => Decide(WalkInAdmissionDecision.Rejected, decidedAtUtc, notes);

    /// <summary>
    /// Status-aware expiry. A Pending request expires at its own short expiry time (or at the clinic day end, which
    /// the caller caps into <see cref="ExpiresAtUtc"/>). An Approved permit is valid for the whole clinic day it
    /// belongs to, regardless of how long after the request it was approved. Both expire at the clinic-day change.
    /// The clinic date is supplied by IClinicClock by the caller, so day boundaries stay DST-safe.
    /// </summary>
    public bool IsExpired(DateTimeOffset currentUtc, DateOnly currentClinicDate, DateOnly queueDate)
    {
        if (currentClinicDate != queueDate)
            return true;

        return Decision == WalkInAdmissionDecision.Pending && currentUtc.ToUniversalTime() >= ExpiresAtUtc;
    }

    public void Expire(DateTimeOffset expiredAtUtc)
    {
        if (Decision != WalkInAdmissionDecision.Pending)
            throw new InvalidOperationException("Only a pending admission request can be marked expired.");
        if (expiredAtUtc.ToUniversalTime() < ExpiresAtUtc)
            throw new InvalidOperationException("An admission request cannot expire before its expiry time.");

        Decision = WalkInAdmissionDecision.Expired;
        DecisionAtUtc = expiredAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Withdraws a Pending request because its walk-in entry was cancelled. Recorded as Expired with the withdrawal
    /// time, so it is never confused with a Reception/Doctor decision. Does not wait for the request's expiry time.
    /// </summary>
    public void WithdrawPending(DateTimeOffset withdrawnAtUtc)
    {
        if (Decision != WalkInAdmissionDecision.Pending)
            throw new InvalidOperationException("Only a pending admission request can be withdrawn.");

        Decision = WalkInAdmissionDecision.Expired;
        DecisionAtUtc = withdrawnAtUtc.ToUniversalTime();
    }

    public void MarkUsed(QueueEntry entry, DateTimeOffset usedAtUtc, DateOnly currentClinicDate)
    {
        if (Decision != WalkInAdmissionDecision.Approved)
            throw new InvalidOperationException("Only an approved admission request can be used.");
        if (QueueEntryId != entry.Id || DoctorId != entry.DoctorId)
            throw new InvalidOperationException("The approval belongs to a different queue entry or doctor.");
        if (UsedAtUtc is not null)
            throw new InvalidOperationException("This approval has already been used.");
        if (IsExpired(usedAtUtc, currentClinicDate, entry.QueueDate))
            throw new InvalidOperationException("This approval has expired.");

        UsedAtUtc = usedAtUtc.ToUniversalTime();
    }

    private void Decide(WalkInAdmissionDecision decision, DateTimeOffset decidedAtUtc, string? notes)
    {
        if (Decision != WalkInAdmissionDecision.Pending)
            throw new InvalidOperationException("Only a pending admission request can be decided.");
        if (decidedAtUtc.ToUniversalTime() >= ExpiresAtUtc)
            throw new InvalidOperationException("The admission request has expired.");

        Decision = decision;
        DecisionAtUtc = decidedAtUtc.ToUniversalTime();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
