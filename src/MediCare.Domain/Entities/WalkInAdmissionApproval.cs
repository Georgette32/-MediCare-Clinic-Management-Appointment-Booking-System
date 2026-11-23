using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Doctor decision on a patient's request to join a clinic queue as a walk-in.
/// </summary>
public sealed class WalkInAdmissionApproval : BaseEntity
{
    private WalkInAdmissionApproval()
    {
        // Required by EF Core.
    }

    public Guid QueueEntryId { get; private set; }

    public QueueEntry QueueEntry { get; private set; } = null!;

    public Guid DoctorProfileId { get; private set; }

    public WalkInAdmissionDecision Decision { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? DecisionAtUtc { get; private set; }

    public DateTime? UsedAtUtc { get; private set; }

    public string? Notes { get; private set; }

    public static WalkInAdmissionApproval Request(QueueEntry queueEntry, DateTime requestedAtUtc, DateTime expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(queueEntry);
        if (queueEntry.Id == Guid.Empty)
        {
            throw new DomainException("An admission approval must be associated with a queue entry.");
        }

        if (queueEntry.DoctorProfileId == Guid.Empty)
        {
            throw new DomainException("An admission approval must be associated with a doctor.");
        }

        if (queueEntry.Source != QueueEntrySource.WalkIn)
        {
            throw new DomainException("Admission approvals can only be requested for walk-in entries.");
        }

        if (expiresAtUtc <= requestedAtUtc)
        {
            throw new DomainException("Approval expiry must follow its request time.");
        }

        return new WalkInAdmissionApproval
        {
            QueueEntryId = queueEntry.Id,
            QueueEntry = queueEntry,
            DoctorProfileId = queueEntry.DoctorProfileId,
            Decision = WalkInAdmissionDecision.Pending,
            RequestedAtUtc = requestedAtUtc.ToUniversalTime(),
            ExpiresAtUtc = expiresAtUtc.ToUniversalTime()
        };
    }

    public void Approve(DateTime decidedAtUtc, string? notes = null) =>
        Decide(WalkInAdmissionDecision.Approved, decidedAtUtc, notes);

    public void Reject(DateTime decidedAtUtc, string? notes = null) =>
        Decide(WalkInAdmissionDecision.Rejected, decidedAtUtc, notes);

    public bool IsExpired(DateTime currentUtc, DateOnly currentClinicDate, DateOnly queueDate) =>
        currentClinicDate != queueDate || currentUtc.ToUniversalTime() >= ExpiresAtUtc;

    public void Expire(DateTime expiredAtUtc)
    {
        if (Decision != WalkInAdmissionDecision.Pending)
        {
            throw new DomainException("Only a pending admission request can be marked expired.");
        }

        if (expiredAtUtc.ToUniversalTime() < ExpiresAtUtc)
        {
            throw new DomainException("An admission request cannot expire before its expiry time.");
        }

        Decision = WalkInAdmissionDecision.Expired;
        DecisionAtUtc = expiredAtUtc.ToUniversalTime();
        MarkUpdated();
    }

    public void MarkUsed(QueueEntry entry, DateTime usedAtUtc, DateOnly currentClinicDate)
    {
        if (Decision != WalkInAdmissionDecision.Approved)
        {
            throw new DomainException("Only an approved admission request can be used.");
        }

        if (QueueEntryId != entry.Id || DoctorProfileId != entry.DoctorProfileId)
        {
            throw new DomainException("The approval belongs to a different queue entry or doctor.");
        }

        if (UsedAtUtc is not null)
        {
            throw new DomainException("This approval has already been used.");
        }

        if (IsExpired(usedAtUtc, currentClinicDate, entry.QueueDate))
        {
            throw new DomainException("This approval has expired.");
        }

        UsedAtUtc = usedAtUtc.ToUniversalTime();
        MarkUpdated();
    }

    private void Decide(WalkInAdmissionDecision decision, DateTime decidedAtUtc, string? notes)
    {
        if (Decision != WalkInAdmissionDecision.Pending)
        {
            throw new DomainException("Only a pending admission request can be decided.");
        }

        if (decidedAtUtc.ToUniversalTime() >= ExpiresAtUtc)
        {
            throw new DomainException("The admission request has expired.");
        }

        Decision = decision;
        DecisionAtUtc = decidedAtUtc.ToUniversalTime();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        MarkUpdated();
    }
}
