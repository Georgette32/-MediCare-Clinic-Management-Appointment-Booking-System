namespace MediCare.Domain;

public sealed class DoctorBreak
{
    public long Id { get; set; }
    public string DoctorId { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool IsRecurring { get; set; }
    public DayOfWeek? RecurringDay { get; set; }
    public TimeOnly? RecurringStart { get; set; }
    public TimeOnly? RecurringEnd { get; set; }
}
