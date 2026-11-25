using System.Security.Claims;
using MediCare.Application.Enums;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Models;

/// <summary>
/// The page a notification refers to, when the current user may open it. Used by the bell popup and the Notifications
/// page so both send people to the same place. Returns null when the user lacks the role that page needs.
/// </summary>
public static class NotificationLinks
{
    public static string? For(string kind, ClaimsPrincipal user, IUrlHelper url)
    {
        var target = TargetFor(kind);
        if (target is null || !target.Roles.Any(user.IsInRole))
            return null;
        return url.Action(target.Action, target.Controller);
    }

    private static LinkTarget? TargetFor(string kind)
    {
        if (kind == nameof(StaffNotificationKind.WalkInAdmissionRequested))
            return new LinkTarget("Doctor", "WalkInAdmissions", [MediCareRoles.Doctor]);
        if (kind is nameof(StaffNotificationKind.WorkingHourExtensionRequested) or nameof(StaffNotificationKind.WorkingHourExtensionWithdrawn))
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
}
