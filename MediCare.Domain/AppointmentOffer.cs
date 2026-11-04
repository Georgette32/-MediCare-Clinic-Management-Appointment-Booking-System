namespace MediCare.Domain;

public sealed class AppointmentOffer
{
    public long Id { get; set; }
    public long WaitingListEntryId { get; set; }
    public WaitingListEntry? WaitingListEntry { get; set; }
    public string PatientId { get; set; } = "";
    public string DoctorId { get; set; } = "";
    public DateTimeOffset SlotStart { get; set; }
    public DateTimeOffset SlotEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SlotReleasedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public string? Outcome { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
