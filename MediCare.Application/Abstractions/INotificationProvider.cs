namespace MediCare.Application.Abstractions;

/// <summary>
/// Delivery channel for notifications. The in-app provider writes Notification rows. Future SMS or e-mail providers
/// plug in here; none exists yet.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Delivers one notification. Returns false when it was already delivered (duplicate DedupKey).</summary>
    Task<bool> DeliverAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}

public sealed record NotificationDelivery(
    Guid RecipientUserId,
    string Kind,
    string Title,
    string Message,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string? DedupKey);
