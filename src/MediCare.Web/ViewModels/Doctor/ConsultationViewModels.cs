namespace MediCare.Web.ViewModels.Doctor;

public sealed class ConsultationsViewModel
{
    public IReadOnlyList<ConsultationQueueItem> Entries { get; set; } =
        Array.Empty<ConsultationQueueItem>();
}

public sealed record ConsultationQueueItem(
    Guid Id,
    string PatientName,
    string Source,
    string Status,
    long? QueueSequence,
    DateTime? CalledAt,
    DateTime? ConsultationStartedAt);
