namespace MediCare.Domain;

public sealed class WaitingListEntry
{
    public long Id { get; set; }
    public string PatientId { get; set; } = "";
    public string? PreferredDoctorId { get; set; }
    public string? SpecializationId { get; set; }
    public DateOnly PreferredDateFrom { get; set; }
    public DateOnly PreferredDateTo { get; set; }
    public TimeOnly? PreferredTimeFrom { get; set; }
    public TimeOnly? PreferredTimeTo { get; set; }
    public WaitingListStatus Status { get; set; } = WaitingListStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<AppointmentOffer> Offers { get; set; } = new List<AppointmentOffer>();
}
