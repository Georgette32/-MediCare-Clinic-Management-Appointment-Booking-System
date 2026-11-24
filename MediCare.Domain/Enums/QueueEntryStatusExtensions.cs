namespace MediCare.Domain.Enums;

public static class QueueEntryStatusExtensions
{
    /// <summary>
    /// Live attendance attempts are the ones that still occupy the queue. Only a live attempt blocks a new
    /// attempt for the same appointment; Cancelled, NoShow and Completed attempts are history.
    /// </summary>
    public static bool IsLive(this QueueEntryStatus status) =>
        status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation;
}
