namespace MediCare.Domain;

public sealed class DoctorLeave
{
    public long Id { get; set; }
    public string DoctorId { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Reason { get; set; } = "";
    public bool IsApproved { get; set; }
}
