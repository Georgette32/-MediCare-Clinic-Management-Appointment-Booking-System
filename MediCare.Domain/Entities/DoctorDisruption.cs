namespace MediCare.Domain.Entities;

/// <summary>
/// Reception/Admin record that a doctor leaves before the end of the working day. Recording blocks the remaining
/// time with dated breaks and compiles a worklist; it never changes the doctor's working hours. At most one
/// disruption exists per doctor and date.
/// </summary>
public sealed class DoctorDisruption
{
    public const int MaxReasonLength = 500;

    private DoctorDisruption() { }

    public static DoctorDisruption Record(Guid doctorId, DateOnly date, TimeOnly fromTime, string reason, Guid recordedByUserId, DateTimeOffset recordedAtUtc)
    {
        if (doctorId == Guid.Empty)
            throw new ArgumentException("A disruption requires a doctor.", nameof(doctorId));
        if (recordedByUserId == Guid.Empty)
            throw new ArgumentException("A disruption requires the recording user.", nameof(recordedByUserId));

        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length == 0)
            throw new ArgumentException("A disruption reason is required.", nameof(reason));
        if (trimmedReason.Length > MaxReasonLength)
            throw new ArgumentException($"A disruption reason cannot exceed {MaxReasonLength} characters.", nameof(reason));

        return new DoctorDisruption
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            Date = date,
            FromTime = fromTime,
            Reason = trimmedReason,
            RecordedByUserId = recordedByUserId,
            RecordedAtUtc = recordedAtUtc.ToUniversalTime()
        };
    }

    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly FromTime { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid RecordedByUserId { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public DateTimeOffset? DayEndRecordedAtUtc { get; private set; }
    public Guid? DayEndRecordedByUserId { get; private set; }

    public Doctor Doctor { get; private set; } = null!;

    public bool IsDayEndRecorded => DayEndRecordedAtUtc is not null;

    /// <summary>Marks that Reception ended the doctor's day with unresolved appointments. Can be recorded once.</summary>
    public void RecordDayEnd(Guid actorUserId, DateTimeOffset recordedAtUtc)
    {
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("Day end requires the recording user.", nameof(actorUserId));
        if (IsDayEndRecorded)
            throw new InvalidOperationException("The day end is already recorded for this disruption.");

        DayEndRecordedAtUtc = recordedAtUtc.ToUniversalTime();
        DayEndRecordedByUserId = actorUserId;
    }
}
