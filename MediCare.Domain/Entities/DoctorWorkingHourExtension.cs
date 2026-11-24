using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

/// <summary>
/// A recorded extension of one doctor working window on one clinic-local date. The extension only adds availability
/// after the window end once it is Approved by the owning Doctor. Pending requests never add availability.
/// </summary>
public sealed class DoctorWorkingHourExtension
{
    public const int MaxReasonLength = 500;
    public const int MaxDecisionNotesLength = 500;

    private DoctorWorkingHourExtension() { }

    /// <summary>Reception/Admin request. Starts Pending and waits for the Doctor's decision.</summary>
    public static DoctorWorkingHourExtension Request(
        Guid doctorId,
        DateOnly date,
        TimeOnly originalEndTime,
        TimeOnly extendedEndTime,
        string reason,
        Guid requestedByUserId,
        DateTimeOffset requestedAtUtc)
    {
        return Create(doctorId, date, originalEndTime, extendedEndTime, reason, requestedByUserId, requestedAtUtc,
            WorkingHourExtensionStatus.Pending, decidedAtUtc: null, decisionNotes: null);
    }

    /// <summary>The Doctor records an extension of their own schedule. This is the recorded Doctor approval.</summary>
    public static DoctorWorkingHourExtension CreateApprovedByDoctor(
        Guid doctorId,
        DateOnly date,
        TimeOnly originalEndTime,
        TimeOnly extendedEndTime,
        string reason,
        Guid doctorUserId,
        DateTimeOffset requestedAtUtc)
    {
        var utc = requestedAtUtc.ToUniversalTime();
        return Create(doctorId, date, originalEndTime, extendedEndTime, reason, doctorUserId, utc,
            WorkingHourExtensionStatus.Approved, decidedAtUtc: utc, decisionNotes: null);
    }

    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly OriginalEndTime { get; private set; }
    public TimeOnly ExtendedEndTime { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public WorkingHourExtensionStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionNotes { get; private set; }

    public Doctor Doctor { get; private set; } = null!;

    /// <summary>True when a Pending request's date is before the clinic-local today. Expiry is not a stored status.</summary>
    public bool IsExpiredPending(DateOnly clinicToday) => Status == WorkingHourExtensionStatus.Pending && Date < clinicToday;

    public void Approve(DateTimeOffset decidedAtUtc, string? notes = null) =>
        Decide(WorkingHourExtensionStatus.Approved, decidedAtUtc, notes);

    public void Reject(DateTimeOffset decidedAtUtc, string? notes = null) =>
        Decide(WorkingHourExtensionStatus.Rejected, decidedAtUtc, notes);

    /// <summary>Cancels a Pending or Approved extension. Stored with DecidedAtUtc as the cancellation time.</summary>
    public void Cancel(DateTimeOffset cancelledAtUtc)
    {
        if (Status is not (WorkingHourExtensionStatus.Pending or WorkingHourExtensionStatus.Approved))
            throw new InvalidOperationException("Only a pending or approved working-hour extension can be cancelled.");
        if (cancelledAtUtc.ToUniversalTime() < RequestedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(cancelledAtUtc), "Cancellation cannot precede the request.");

        Status = WorkingHourExtensionStatus.Cancelled;
        DecidedAtUtc = cancelledAtUtc.ToUniversalTime();
    }

    private void Decide(WorkingHourExtensionStatus decision, DateTimeOffset decidedAtUtc, string? notes)
    {
        if (Status != WorkingHourExtensionStatus.Pending)
            throw new InvalidOperationException("Only a pending working-hour extension can be decided.");
        if (decidedAtUtc.ToUniversalTime() < RequestedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(decidedAtUtc), "A decision cannot precede the request.");
        if (notes is not null && notes.Trim().Length > MaxDecisionNotesLength)
            throw new ArgumentException($"Decision notes cannot exceed {MaxDecisionNotesLength} characters.", nameof(notes));

        Status = decision;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        DecisionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private static DoctorWorkingHourExtension Create(
        Guid doctorId,
        DateOnly date,
        TimeOnly originalEndTime,
        TimeOnly extendedEndTime,
        string reason,
        Guid requestedByUserId,
        DateTimeOffset requestedAtUtc,
        WorkingHourExtensionStatus status,
        DateTimeOffset? decidedAtUtc,
        string? decisionNotes)
    {
        if (doctorId == Guid.Empty)
            throw new ArgumentException("An extension must belong to a doctor.", nameof(doctorId));
        if (requestedByUserId == Guid.Empty)
            throw new ArgumentException("An extension must record who requested it.", nameof(requestedByUserId));
        if (extendedEndTime <= originalEndTime)
            throw new ArgumentException("The extended end time must be later than the original window end.", nameof(extendedEndTime));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a working-hour extension.", nameof(reason));
        if (reason.Trim().Length > MaxReasonLength)
            throw new ArgumentException($"The reason cannot exceed {MaxReasonLength} characters.", nameof(reason));

        return new DoctorWorkingHourExtension
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            Date = date,
            OriginalEndTime = originalEndTime,
            ExtendedEndTime = extendedEndTime,
            Reason = reason.Trim(),
            Status = status,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = requestedAtUtc.ToUniversalTime(),
            DecidedAtUtc = decidedAtUtc,
            DecisionNotes = decisionNotes
        };
    }
}
