namespace MediCare.Domain;

public sealed class DoctorQueueSession
{
    public long Id { get; set; }
    public string DoctorId { get; set; } = "";
    public DateOnly SessionDate { get; set; }
    public DoctorQueueSessionStatus Status { get; set; } = DoctorQueueSessionStatus.NotStarted;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<QueueEntry> QueueEntries { get; set; } = new List<QueueEntry>();
}
