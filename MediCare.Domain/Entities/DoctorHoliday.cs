namespace MediCare.Domain.Entities;

public sealed class DoctorHoliday
{
    private DoctorHoliday() { }

    public static DoctorHoliday Create(Guid doctorId, DateOnly startDate, DateOnly endDate, string? reason)
    {
        if (endDate < startDate)
            throw new ArgumentException("Holiday end date cannot be before its start date.", nameof(endDate));

        return new DoctorHoliday
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = reason
        };
    }

    public Guid Id { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string? Reason { get; private set; }

    public Doctor Doctor { get; private set; } = null!;
}
