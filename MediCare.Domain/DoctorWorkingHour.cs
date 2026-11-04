namespace MediCare.Domain;

public sealed class DoctorWorkingHour
{
    public long Id { get; set; }
    public string DoctorId { get; set; } = "";
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
