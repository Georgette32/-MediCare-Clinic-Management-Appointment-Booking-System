using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>Patient booking, own appointments, and patient-side cancel/reschedule. Every action acts on the Patient linked to the signed-in user.</summary>
[Authorize(Roles = MediCareRoles.Patient)]
public sealed class AppointmentsController(
    IAppointmentBookingFlow booking,
    AppointmentService appointments,
    IAppointmentQueryService queries,
    IDoctorDirectoryService directory,
    IDoctorAvailabilityService availability,
    IClinicClock clock) : Controller
{
    private const int MaxReasonLength = 500;

    [HttpGet]
    public async Task<IActionResult> Book(Guid doctorId, DateOnly date, TimeOnly time, CancellationToken cancellationToken)
    {
        if (await ResolvePatientIdAsync(cancellationToken) is null)
            return Forbid();

        var model = await BuildConfirmationAsync(doctorId, date, time, error: null, alternatives: [], cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(BookingForm form, CancellationToken cancellationToken)
    {
        if (await ResolvePatientIdAsync(cancellationToken) is not Guid patientId)
            return Forbid();
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Mine));

        var outcome = await booking.RequestForPatientAsync(patientId, form.DoctorId, form.Date, form.Time, cancellationToken);
        if (outcome.Succeeded)
        {
            TempData["StatusMessage"] = "Request sent; Reception will confirm it.";
            return RedirectToAction(nameof(Mine));
        }

        var model = await BuildConfirmationAsync(form.DoctorId, form.Date, form.Time, outcome.Error, outcome.Alternatives, cancellationToken);
        return model is null ? NotFound() : View(nameof(Book), model);
    }

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        if (await ResolvePatientIdAsync(cancellationToken) is not Guid patientId)
            return Forbid();

        return View(await queries.GetPatientBoardAsync(patientId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(PatientCancelForm form, CancellationToken cancellationToken)
    {
        if (await ResolvePatientIdAsync(cancellationToken) is not Guid patientId)
            return Forbid();

        if (!ModelState.IsValid || (form.Reason?.Length ?? 0) > MaxReasonLength)
        {
            TempData["ErrorMessage"] = "Check the cancellation details and try again.";
            return RedirectToAction(nameof(Mine));
        }

        try
        {
            var reason = string.IsNullOrWhiteSpace(form.Reason) ? null : form.Reason.Trim();
            await appointments.CancelByPatientAsync(form.AppointmentId, patientId, reason, cancellationToken);
            TempData["StatusMessage"] = "Appointment cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Mine));
    }

    [HttpGet]
    public async Task<IActionResult> Reschedule(Guid id, Guid? doctorId, DateOnly? date, CancellationToken cancellationToken)
    {
        if (await ResolvePatientIdAsync(cancellationToken) is not Guid patientId)
            return Forbid();

        var original = await queries.GetOwnedAppointmentAsync(id, patientId, cancellationToken);
        if (original is null)
            return NotFound();
        if (!original.CanPatientReschedule)
        {
            TempData["ErrorMessage"] = "This appointment can no longer be rescheduled.";
            return RedirectToAction(nameof(Mine));
        }

        var model = await BuildRescheduleAsync(original, doctorId, date, error: null, alternatives: [], cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(RescheduleForm form, CancellationToken cancellationToken)
    {
        if (await ResolvePatientIdAsync(cancellationToken) is not Guid patientId)
            return Forbid();

        try
        {
            var outcome = await booking.RescheduleForPatientAsync(form.AppointmentId, patientId, form.DoctorId, form.Date, form.Time, cancellationToken);
            if (outcome.Succeeded)
            {
                TempData["StatusMessage"] = "Reschedule request sent. Your current time stays booked until Reception confirms the new one.";
                return RedirectToAction(nameof(Mine));
            }

            var original = await queries.GetOwnedAppointmentAsync(form.AppointmentId, patientId, cancellationToken);
            if (original is null)
                return NotFound();
            var model = await BuildRescheduleAsync(original, form.DoctorId, form.Date, outcome.Error, outcome.Alternatives, cancellationToken);
            return View(nameof(Reschedule), model);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
    }

    private async Task<Guid?> ResolvePatientIdAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return null;
        return await queries.GetPatientIdForUserAsync(userId, cancellationToken);
    }

    private async Task<BookingConfirmationModel?> BuildConfirmationAsync(
        Guid doctorId,
        DateOnly date,
        TimeOnly time,
        string? error,
        IReadOnlyList<TimeOnly> alternatives,
        CancellationToken cancellationToken)
    {
        var profile = await directory.GetProfileAsync(doctorId, date, cancellationToken);
        if (profile is null)
            return null;

        return new BookingConfirmationModel
        {
            Profile = profile,
            Date = date,
            Time = time,
            EndTime = time.AddMinutes(profile.AppointmentDurationOnDateMinutes),
            IsSlotFree = profile.FreeSlots.Contains(time),
            Error = error,
            Alternatives = alternatives
        };
    }

    private async Task<RescheduleModel> BuildRescheduleAsync(
        AppointmentRow original,
        Guid? doctorId,
        DateOnly? date,
        string? error,
        IReadOnlyList<TimeOnly> alternatives,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var doctors = await directory.SearchAsync(null, null, null, cancellationToken);
        var selectedDoctor = doctorId is Guid chosen && doctors.Any(item => item.DoctorId == chosen)
            ? chosen
            : original.Record.DoctorId;
        var selectedDate = date ?? original.Record.Date;
        var slots = selectedDate < today
            ? []
            : await availability.GetAvailableSlotsAsync(selectedDoctor, selectedDate, cancellationToken);

        return new RescheduleModel
        {
            AppointmentId = original.Record.Id,
            OriginalDoctorName = original.Record.DoctorName,
            OriginalDate = original.Record.Date,
            OriginalStartTime = original.Record.StartTime,
            Doctors = doctors,
            DoctorId = selectedDoctor,
            Date = selectedDate,
            Today = today,
            Slots = slots,
            Error = error,
            Alternatives = alternatives
        };
    }
}
