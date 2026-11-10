namespace MediCare.Domain;

public sealed class Appointment
{
    public long Id { get; set; }
    public string PatientId { get; set; } = "";
    public string DoctorId { get; set; } = "";
    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset ScheduledEnd { get; set; }
    public int DurationSnapshotMinutes { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    public DateTimeOffset? ConsultationStartedAt { get; set; }
    public DateTimeOffset? ConsultationEndedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTimeOffset? NoShowAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<QueueEntry> QueueEntries { get; set; } = new List<QueueEntry>();
}
