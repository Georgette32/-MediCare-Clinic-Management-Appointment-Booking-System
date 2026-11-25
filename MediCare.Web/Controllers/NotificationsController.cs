using System.Security.Claims;
using MediCare.Application.Services;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>
/// The signed-in user's in-app notifications. Every action works on the current user's own rows only: the service refuses
/// ids that belong to someone else. Links to related pages are shown only when the user holds the role that page needs.
/// </summary>
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : Controller
{
    /// <summary>The page shows one stacked list of the latest notifications (no pages to click through).</summary>
    public const int ListSize = NotificationService.MaxPageSize;

    [HttpGet("Notifications")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var result = await notifications.GetForUserAsync(userId, 1, ListSize, cancellationToken);
        var items = result.Items
            .Select(item => new NotificationListItem(
                item.Id,
                item.Title,
                item.Message,
                item.CreatedAtUtc,
                item.ReadAtUtc is null,
                NotificationLinks.For(item.Kind, User, Url)))
            .ToArray();

        return View(new NotificationsPageModel(items, result.TotalCount));
    }

    /// <summary>
    /// Marks one notification read, then sends the user to the related page. When it has no page for this user, they go
    /// back to where they opened it (the bell popup passes the current page) or to the list.
    /// </summary>
    [HttpPost("Notifications/{id:guid}/Open")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(Guid id, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var item = await notifications.MarkReadAsync(id, userId, cancellationToken);
        if (item is null)
            return NotFound();

        var link = NotificationLinks.For(item.Kind, User, Url);
        if (link is not null)
            return Redirect(link);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
    }

    [HttpPost("Notifications/MarkAllRead")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(string? returnUrl, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var changed = await notifications.MarkAllReadAsync(userId, cancellationToken);
        TempData["StatusMessage"] = changed == 0 ? "No unread notifications." : $"{changed} notification{(changed == 1 ? "" : "s")} marked as read.";
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
    }

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
