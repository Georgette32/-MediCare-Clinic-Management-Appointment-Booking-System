using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>
/// Schedule edits for a Doctor (own profile) or an Admin (any doctor). The role check happens here; ownership is
/// enforced by <see cref="IDoctorScheduleService"/>, which reloads the doctor and refuses foreign profiles.
/// </summary>
[Authorize(Roles = MediCareRoles.Doctor + "," + MediCareRoles.Admin)]
public sealed class ScheduleManagementController(IDoctorScheduleService schedule, IClinicClock clock) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddWorkingWindow(Guid doctorId, DayOfWeek? day, TimeOnly? startTime, TimeOnly? endTime, CancellationToken cancellationToken) =>
        RunAsync(doctorId, day is null || startTime is null || endTime is null, "Choose a day and both start and end times.",
            actor => schedule.AddWorkingWindowAsync(actor, doctorId, day!.Value, startTime!.Value, endTime!.Value, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ChangeWorkingWindow(Guid doctorId, Guid workingWindowId, TimeOnly? startTime, TimeOnly? endTime, CancellationToken cancellationToken) =>
        RunAsync(doctorId, startTime is null || endTime is null, "Enter both start and end times.",
            actor => schedule.ChangeWorkingWindowTimesAsync(actor, doctorId, workingWindowId, startTime!.Value, endTime!.Value, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SetWorkingWindowActive(Guid doctorId, Guid workingWindowId, bool isActive, CancellationToken cancellationToken) =>
        RunAsync(doctorId, false, null,
            actor => schedule.SetWorkingWindowActiveAsync(actor, doctorId, workingWindowId, isActive, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ChangeAppointmentDuration(Guid doctorId, int? minutes, DateOnly? effectiveFrom, CancellationToken cancellationToken) =>
        RunAsync(doctorId, minutes is null, "Enter the appointment duration in minutes.",
            actor => schedule.ChangeAppointmentDurationAsync(actor, doctorId, minutes!.Value, effectiveFrom ?? clock.Today.AddDays(1), cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CancelAppointmentDurationChange(Guid doctorId, CancellationToken cancellationToken) =>
        RunAsync(doctorId, false, null,
            actor => schedule.CancelScheduledDurationChangeAsync(actor, doctorId, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddBreak(Guid doctorId, DateOnly? date, TimeOnly? startTime, TimeOnly? endTime, CancellationToken cancellationToken) =>
        RunAsync(doctorId, date is null || startTime is null || endTime is null, "Choose a date and both start and end times.",
            actor => schedule.AddBreakAsync(actor, doctorId, date!.Value, startTime!.Value, endTime!.Value, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveBreak(Guid doctorId, Guid breakId, CancellationToken cancellationToken) =>
        RunAsync(doctorId, false, null,
            actor => schedule.RemoveBreakAsync(actor, doctorId, breakId, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddHoliday(Guid doctorId, DateOnly? startDate, DateOnly? endDate, string? reason, CancellationToken cancellationToken) =>
        RunAsync(doctorId, startDate is null || endDate is null, "Choose a start and an end date.",
            actor => schedule.AddHolidayAsync(actor, doctorId, startDate!.Value, endDate!.Value, reason, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveHoliday(Guid doctorId, Guid holidayId, CancellationToken cancellationToken) =>
        RunAsync(doctorId, false, null,
            actor => schedule.RemoveHolidayAsync(actor, doctorId, holidayId, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateBiography(Guid doctorId, string? biography, CancellationToken cancellationToken) =>
        RunAsync(doctorId, false, null,
            actor => schedule.UpdateBiographyAsync(actor, doctorId, biography, cancellationToken));

    private async Task<IActionResult> RunAsync(Guid doctorId, bool missingInput, string? missingMessage, Func<ScheduleActor, Task<ManagementResult>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Forbid();

        if (missingInput)
        {
            TempData["ErrorMessage"] = missingMessage;
            return RedirectToScheduleFor(doctorId);
        }

        try
        {
            var actor = new ScheduleActor(userId, User.IsInRole(MediCareRoles.Admin));
            var result = await action(actor);
            TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Message;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return RedirectToScheduleFor(doctorId);
    }

    private IActionResult RedirectToScheduleFor(Guid doctorId) =>
        User.IsInRole(MediCareRoles.Admin)
            ? RedirectToAction("DoctorSchedule", "Admin", new { doctorId })
            : RedirectToAction("Schedule", "Doctor");
}
