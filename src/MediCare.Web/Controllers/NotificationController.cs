
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace MediCare.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserService _currentUser;

    public NotificationController(
        INotificationService notificationService,
        ICurrentUserService currentUser)
    {
        _notificationService = notificationService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Challenge();

        var notifications =
            await _notificationService.GetUserNotificationsAsync(
                userId.Value);

        return Ok(notifications);
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Challenge();

        var count =
            await _notificationService.GetUnreadCountAsync(
                userId.Value);

        return Ok(new { unreadCount = count });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Challenge();

        var result =
            await _notificationService.MarkAsReadAsync(
                id,
                userId.Value);

        if (!result)
            return NotFound();

        return Ok(new { success = true });
    }
}
