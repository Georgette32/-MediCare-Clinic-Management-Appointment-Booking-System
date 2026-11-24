using System.Security.Claims;
using MediCare.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.ViewComponents;

/// <summary>The navigation bell: one cheap unread count for the signed-in user. Renders nothing for anonymous visitors.</summary>
public sealed class NotificationBellViewComponent(INotificationService notifications) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Content(string.Empty);
        }

        var unread = await notifications.CountUnreadAsync(userId, HttpContext.RequestAborted);
        return View(unread);
    }
}
