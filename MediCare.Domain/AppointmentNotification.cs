namespace MediCare.Domain;

public sealed class AppointmentNotification
{
    public long Id { get; set; }
    public string PatientId { get; set; } = "";
    public long? OfferId { get; set; }
    public string Type { get; set; } = "AppointmentOffer";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
