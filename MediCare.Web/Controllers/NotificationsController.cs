using System.Security.Claims;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
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
    private const int PageSize = 20;

    [HttpGet("Notifications")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var result = await notifications.GetForUserAsync(userId, page, PageSize, cancellationToken);
        var items = result.Items
            .Select(item => new NotificationListItem(
                item.Id,
                item.Title,
                item.Message,
                item.CreatedAtUtc,
                item.ReadAtUtc is null,
                LinkFor(item.Kind)))
            .ToArray();

        return View(new NotificationsPageModel(items, result.Page, result.PageSize, result.TotalCount));
    }

    /// <summary>Marks one notification read, then sends the user to the related page (or back to the list).</summary>
    [HttpPost("Notifications/{id:guid}/Open")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var item = await notifications.MarkReadAsync(id, userId, cancellationToken);
        if (item is null)
            return NotFound();

        var link = LinkFor(item.Kind);
        return link is null ? RedirectToAction(nameof(Index)) : Redirect(link);
    }

    [HttpPost("Notifications/MarkAllRead")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var changed = await notifications.MarkAllReadAsync(userId, cancellationToken);
        TempData["StatusMessage"] = changed == 0 ? "No unread notifications." : $"{changed} notification(s) marked as read.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The page a notification refers to, when the current user may open it. Patient kinds go to My appointments; the
    /// staff kinds go to the doctor's or Reception's list. Returns null when the user lacks the role.
    /// </summary>
    private string? LinkFor(string kind)
    {
        var target = TargetFor(kind);
        if (target is null || !target.Roles.Any(User.IsInRole))
            return null;

        return Url.Action(target.Action, target.Controller);
    }

    private static LinkTarget? TargetFor(string kind)
    {
        if (kind == nameof(StaffNotificationKind.WalkInAdmissionRequested))
            return new LinkTarget("Doctor", "WalkInAdmissions", [MediCareRoles.Doctor]);
        if (kind == nameof(StaffNotificationKind.WorkingHourExtensionRequested))
            return new LinkTarget("Doctor", "Extensions", [MediCareRoles.Doctor]);
        if (kind == nameof(StaffNotificationKind.WorkingHourExtensionWithdrawn))
            return new LinkTarget("Doctor", "Extensions", [MediCareRoles.Doctor]);
        if (kind is nameof(StaffNotificationKind.WorkingHourExtensionDecided) or nameof(StaffNotificationKind.WorkingHourExtensionCancelled))
            return new LinkTarget("Reception", "Extensions", [MediCareRoles.Receptionist, MediCareRoles.Admin]);
        if (kind is nameof(StaffNotificationKind.AppointmentRequested) or nameof(StaffNotificationKind.RescheduleRequested))
            return new LinkTarget("Reception", "Index", [MediCareRoles.Receptionist, MediCareRoles.Admin]);
        if (kind == nameof(StaffNotificationKind.WalkInAdmissionDecided))
            return new LinkTarget("Reception", "Queue", [MediCareRoles.Receptionist, MediCareRoles.Admin]);
        if (Enum.TryParse<PatientNotificationKind>(kind, out _))
            return new LinkTarget("Appointments", "Mine", [MediCareRoles.Patient]);
        return null;
    }

    private sealed record LinkTarget(string Controller, string Action, string[] Roles);

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
