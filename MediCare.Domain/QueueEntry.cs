namespace MediCare.Domain;

public sealed class QueueEntry
{
    public long Id { get; set; }
    public string DoctorId { get; set; } = "";
    public string PatientId { get; set; } = "";
    public long? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    public long? DoctorQueueSessionId { get; set; }
    public DoctorQueueSession? DoctorQueueSession { get; set; }
    public QueueType Type { get; set; }
    public QueueStatus Status { get; set; } = QueueStatus.Waiting;
    public WalkInApproval Approval { get; set; } = WalkInApproval.Pending;
    public DateTimeOffset ArrivalTime { get; set; }
    public DateTimeOffset? CheckInTime { get; set; }
    public string? ApprovedByDoctorId { get; set; }
    public DateTimeOffset? ApprovalTime { get; set; }
    public DateTimeOffset? CalledAt { get; set; }
    public DateTimeOffset? CallExpiresAt { get; set; }
    public DateTimeOffset? EnteredConsultationAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
