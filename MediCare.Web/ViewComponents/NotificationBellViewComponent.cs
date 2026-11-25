using System.Security.Claims;
using MediCare.Application.Services;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.ViewComponents;

/// <summary>
/// The top-bar bell: the unread count plus the newest notifications for its popup, so people can read and open them
/// without leaving the page. Renders nothing for anonymous visitors.
/// </summary>
public sealed class NotificationBellViewComponent(INotificationService notifications) : ViewComponent
{
    public const int PopupSize = 15;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Content(string.Empty);
        }

        var cancellationToken = HttpContext.RequestAborted;
        var unread = await notifications.CountUnreadAsync(userId, cancellationToken);
        var latest = await notifications.GetForUserAsync(userId, 1, PopupSize, cancellationToken);
        var items = latest.Items
            .Select(item => new NotificationListItem(
                item.Id, item.Title, item.Message, item.CreatedAtUtc, item.ReadAtUtc is null,
                NotificationLinks.For(item.Kind, UserClaimsPrincipal, Url)))
            .ToArray();
        return View(new NotificationBellModel(unread, items, latest.TotalCount));
    }
}
