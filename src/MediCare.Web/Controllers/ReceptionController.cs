using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Services;
using MediCare.Web.ViewModels.Reception;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class ReceptionController(
    AppointmentService appointmentService,
    ICheckInService checkInService,
    IWalkInAdmissionApprovalService admissionApprovalService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var appointments = await appointmentService.GetPendingAsync(cancellationToken);
        return View(appointments);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await appointmentService.ConfirmAsync(id, cancellationToken);
            TempData["StatusMessage"] = "Appointment confirmed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await appointmentService.RejectAsync(id, cancellationToken);
            TempData["StatusMessage"] = "Appointment rejected.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduledCheckIn(
        ScheduledCheckInViewModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await checkInService.CheckInScheduledPatientAsync(
                model.AppointmentId, model.ExpectedDoctorId, cancellationToken);
            TempData["StatusMessage"] = result.Message ?? "Scheduled patient checked in.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterWalkIn(
        WalkInRegistrationViewModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            await checkInService.RegisterWalkInAsync(model.PatientId, model.DoctorId, cancellationToken);
            TempData["StatusMessage"] = "Walk-in registered in the waiting queue.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestWalkInAdmission(
        WalkInAdmissionRequestViewModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            await admissionApprovalService.RequestAsync(model.QueueEntryId, cancellationToken);
            TempData["StatusMessage"] = "Walk-in admission request sent to the doctor.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CallWalkIn(
        WalkInCallViewModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await admissionApprovalService.CallAsync(
                model.QueueEntryId, model.ApprovalId, cancellationToken);
            TempData["StatusMessage"] = result.Message;
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
