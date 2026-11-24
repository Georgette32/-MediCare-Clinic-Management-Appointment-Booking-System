namespace MediCare.Application.Services;

/// <summary>
/// Notification facade. Notify* methods return false when a notification with the same DedupKey already exists for the
/// recipient (idempotent delivery). They throw when the recipient cannot be resolved or delivery fails; callers after a
/// committed change go through the publishers, which log and swallow such failures.
/// </summary>
public interface INotificationService
{
    Task<bool> NotifyUserAsync(Guid userId, NotificationDraft draft, CancellationToken cancellationToken);

    Task<bool> NotifyPatientAsync(Guid patientId, NotificationDraft draft, CancellationToken cancellationToken);

    Task<bool> NotifyDoctorAsync(Guid doctorId, NotificationDraft draft, CancellationToken cancellationToken);

    Task<NotificationPage> GetForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Unread count for the signed-in user (drives the bell in the layout).</summary>
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Marks one notification read and returns it. Returns null when it does not exist or is not addressed to this user.</summary>
    Task<NotificationItem?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Marks every unread notification of the user read and returns how many changed.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record NotificationDraft(
    string Kind,
    string Title,
    string Message,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    string? DedupKey = null);

public sealed record NotificationItem(
    Guid Id,
    string Kind,
    string Title,
    string Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc,
    string? RelatedEntityType,
    Guid? RelatedEntityId);

public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, int TotalCount, int Page, int PageSize);
