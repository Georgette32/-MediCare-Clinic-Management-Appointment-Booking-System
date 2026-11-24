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

    /// <summary>A dated block created by a doctor-leaves-early disruption. Existing breaks leave this null.</summary>
    public static DoctorBreak CreateForDisruption(Guid doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime, Guid disruptionId)
    {
        if (disruptionId == Guid.Empty)
            throw new ArgumentException("A disruption block requires its disruption.", nameof(disruptionId));

        var item = Create(doctorId, date, startTime, endTime);
        item.DisruptionId = disruptionId;
        return item;
    }

    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public Guid? DisruptionId { get; private set; }

    public Doctor Doctor { get; private set; } = null!;
}
