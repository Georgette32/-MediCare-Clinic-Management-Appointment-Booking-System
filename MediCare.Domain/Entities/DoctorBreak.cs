namespace MediCare.Domain.Entities;

public sealed class DoctorBreak
{
    private DoctorBreak() { }

    public static DoctorBreak Create(Guid doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException("Break end must be later than its start.", nameof(endTime));

        return new DoctorBreak
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public Doctor Doctor { get; private set; } = null!;
}
