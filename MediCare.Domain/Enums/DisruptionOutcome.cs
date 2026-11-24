namespace MediCare.Domain.Enums;

/// <summary>Resolution state of one disruption worklist item. Pending and Contacted are open; the others are closed.</summary>
public enum DisruptionOutcome
{
    Pending,
    Contacted,
    Rescheduled,
    Cancelled
}
