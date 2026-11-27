namespace MediCare.Domain.Entities;

/// <summary>
/// Append-only history of a working window. A row records the window's state from <see cref="EffectiveFrom"/> onward.
/// Analytics reads the snapshot in force on a date to measure past open time, so a later schedule change does not
/// rewrite it. A window with no snapshots is a legacy window: analytics keeps using its current state for it.
/// </summary>
public sealed class DoctorWorkingHourSnapshot
{
    private DoctorWorkingHourSnapshot() { }

    /// <summary>
    /// Copies the window's current state. <paramref name="effectiveFrom"/> is the clinic-local date from which this state
    /// applies; <see cref="DateOnly.MinValue"/> marks a baseline for the state before history was recorded.
    /// </summary>
    public static DoctorWorkingHourSnapshot Capture(DoctorWorkingHour window, DateOnly effectiveFrom, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new DoctorWorkingHourSnapshot
        {
            Id = Guid.NewGuid(),
            WorkingHourId = window.Id,
            DoctorId = window.DoctorId,
            DayOfWeek = window.DayOfWeek,
            StartTime = window.StartTime,
            EndTime = window.EndTime,
            IsActive = window.IsActive,
            EffectiveFrom = effectiveFrom,
            RecordedAtUtc = recordedAtUtc.ToUniversalTime()
        };
    }

    public Guid Id { get; private set; }
    public Guid WorkingHourId { get; private set; }
    public Guid DoctorId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool IsActive { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public DoctorWorkingHour WorkingHour { get; private set; } = null!;
}
