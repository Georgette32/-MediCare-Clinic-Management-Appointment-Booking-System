using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

public sealed class NotificationService(
    INotificationRepository notifications,
    INotificationProvider provider,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : INotificationService
{
    public const int MaxPageSize = 100;

    public Task<bool> NotifyUserAsync(Guid userId, NotificationDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return DeliverAsync(userId, draft, cancellationToken);
    }

    public async Task<bool> NotifyPatientAsync(Guid patientId, NotificationDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var userId = await notifications.GetPatientUserIdAsync(patientId, cancellationToken)
            ?? throw new InvalidOperationException("The patient was not found.");
        return await DeliverAsync(userId, draft, cancellationToken);
    }

    public async Task<bool> NotifyDoctorAsync(Guid doctorId, NotificationDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var userId = await notifications.GetDoctorUserIdAsync(doctorId, cancellationToken)
            ?? throw new InvalidOperationException("The doctor was not found.");
        return await DeliverAsync(userId, draft, cancellationToken);
    }

    public async Task<int> NotifyReceptionAsync(NotificationDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var delivered = 0;
        foreach (var userId in await notifications.GetActiveReceptionUserIdsAsync(cancellationToken))
        {
            if (await DeliverAsync(userId, draft, cancellationToken))
                delivered++;
        }
        return delivered;
    }

    public async Task<NotificationPage> GetForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var (items, total) = await notifications.GetPageForUserAsync(userId, safePage, safeSize, cancellationToken);
        return new NotificationPage(
            items.Select(ToItem).ToArray(),
            total,
            safePage,
            safeSize);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        notifications.CountUnreadAsync(userId, cancellationToken);

    public async Task<NotificationItem?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetForRecipientAsync(notificationId, userId, cancellationToken);
        if (notification is null)
            return null;

        notification.MarkRead(clinicClock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToItem(notification);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var unread = await notifications.GetUnreadForUserAsync(userId, cancellationToken);
        if (unread.Count == 0)
            return 0;

        var now = clinicClock.UtcNow;
        foreach (var notification in unread)
            notification.MarkRead(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }

    private static NotificationItem ToItem(Notification item) =>
        new(item.Id, item.Kind, item.Title, item.Message, item.CreatedAtUtc, item.ReadAtUtc, item.RelatedEntityType, item.RelatedEntityId);

    private async Task<bool> DeliverAsync(Guid userId, NotificationDraft draft, CancellationToken cancellationToken)
    {
        var dedupKey = string.IsNullOrWhiteSpace(draft.DedupKey) ? null : draft.DedupKey.Trim();
        if (dedupKey is not null && await notifications.ExistsForDedupKeyAsync(userId, dedupKey, cancellationToken))
            return false;

        return await provider.DeliverAsync(
            new NotificationDelivery(userId, draft.Kind, draft.Title, draft.Message, draft.RelatedEntityType, draft.RelatedEntityId, dedupKey),
            cancellationToken);
    }
}
