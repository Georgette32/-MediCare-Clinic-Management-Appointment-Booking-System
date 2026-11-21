using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = MediCareRoles.Receptionist)]
public sealed class ReceptionController(AppointmentService appointmentService) : Controller
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

}
