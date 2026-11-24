namespace MediCare.Domain.Entities;

public sealed class DoctorWorkingHour
{
    private DoctorWorkingHour() { }

    public static DoctorWorkingHour Create(Guid doctorId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        ValidateRange(startTime, endTime);

        return new DoctorWorkingHour
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            IsActive = true
        };
    }

    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool IsActive { get; private set; }

    public Doctor Doctor { get; private set; } = null!;

    /// <summary>Changes the window times. Overlap and impact on booked appointments are checked by the schedule service.</summary>
    public void ChangeTimes(TimeOnly startTime, TimeOnly endTime)
    {
        ValidateRange(startTime, endTime);
        StartTime = startTime;
        EndTime = endTime;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>True when the half-open ranges [start, end) of two windows share any time.</summary>
    public static bool Overlaps(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB) =>
        startA < endB && endA > startB;

    private static void ValidateRange(TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException("Working-hour end must be later than its start.", nameof(endTime));
    }
}
