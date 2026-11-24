namespace MediCare.Web.Models;

public sealed record NotificationListItem(
    Guid Id,
    string Title,
    string Message,
    DateTimeOffset CreatedAtUtc,
    bool IsUnread,
    string? Link);

public sealed record NotificationsPageModel(IReadOnlyList<NotificationListItem> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
