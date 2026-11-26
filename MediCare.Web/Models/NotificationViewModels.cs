namespace MediCare.Web.Models;

public sealed record NotificationListItem(
    Guid Id,
    string Title,
    string Message,
    DateTimeOffset CreatedAtUtc,
    bool IsUnread,
    string? Link);

/// <summary>The Notifications page: one stacked list of the latest items. TotalCount tells whether older ones exist.</summary>
public sealed record NotificationsPageModel(IReadOnlyList<NotificationListItem> Items, int TotalCount)
{
    public bool HasOlder => TotalCount > Items.Count;
}

/// <summary>The bell in the top bar: the unread count and the newest notifications for the popup.</summary>
public sealed record NotificationBellModel(int UnreadCount, IReadOnlyList<NotificationListItem> Items, int TotalCount);

/// <summary>"Just now", "12 min ago", "Today 14:05", "Yesterday 18:00", "21 Nov 09:30": how long ago, in clinic time.</summary>
public static class NotificationTime
{
    public static string Describe(DateTimeOffset createdUtc, DateTimeOffset nowUtc, TimeZoneInfo clinicZone)
    {
        var age = nowUtc - createdUtc;
        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes} min ago";

        var created = TimeZoneInfo.ConvertTime(createdUtc, clinicZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, clinicZone).DateTime);
        var day = DateOnly.FromDateTime(created.DateTime);
        if (day == today)
            return $"Today {created.ToString(Clock.Format, Clock.Culture)}";
        if (day == today.AddDays(-1))
            return $"Yesterday {created.ToString(Clock.Format, Clock.Culture)}";
        return created.Year == TimeZoneInfo.ConvertTime(nowUtc, clinicZone).Year
            ? created.ToString("d MMM h:mm tt", Clock.Culture)
            : created.ToString("d MMM yyyy");
    }
}
