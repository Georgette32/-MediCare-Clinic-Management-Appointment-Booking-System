namespace MediCare.Domain;

public sealed class AppointmentEvent
{
    public long Id { get; set; }
    public string DoctorId { get; set; } = "";
    public string? PatientId { get; set; }
    public long? AppointmentId { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Details { get; set; }
}
