using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Services;
using MediCare.Domain.Exceptions;
using MediCare.Web.Helpers;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public sealed class AppointmentsController(
    IAppointmentRepository appointments,
    AppointmentService appointmentService,
    ICurrentUserService currentUser,
    IClinicClock clock) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        var doctor = await appointments.GetDoctorProfileByUserIdAsync(userId.Value, cancellationToken);
        if (doctor is null)
        {
            return NotFound();
        }

        var items = await DoctorAppointmentLoader.LoadAsync(
            appointments, doctor.Id, clock.Today, days: 30, cancellationToken);

        return View(new DoctorAppointmentsViewModel { Appointments = items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        Guid id,
        string? doctorNotes,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        try
        {
            await appointmentService.CompleteByDoctorAsync(
                id, userId.Value, doctorNotes, cancellationToken);
            TempData["StatusMessage"] =
                "Consultation completed. The patient is now available in My patients.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }
        catch (DomainException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
