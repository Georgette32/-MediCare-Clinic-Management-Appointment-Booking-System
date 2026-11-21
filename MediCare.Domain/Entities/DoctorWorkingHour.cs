namespace MediCare.Domain.Entities;

public sealed class DoctorWorkingHour
{
    private DoctorWorkingHour() { }

    public static DoctorWorkingHour Create(Guid doctorId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException("Working-hour end must be later than its start.", nameof(endTime));

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
}
