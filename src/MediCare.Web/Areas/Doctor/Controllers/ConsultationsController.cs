using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Services;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public sealed class ConsultationsController(
    IQueueRepository queues,
    IAppointmentRepository appointments,
    ConsultationService consultations,
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
            return Forbid();
        }

        var entries = await queues.GetActiveEntriesForDoctorAndDateAsync(
            doctor.Id, clock.Today, cancellationToken);

        var items = new List<ConsultationQueueItem>();
        foreach (var entry in entries)
        {
            var patientName = await appointments.GetPatientNameAsync(
                entry.PatientProfileId, cancellationToken);

            items.Add(new ConsultationQueueItem(
                entry.Id,
                patientName ?? "Unknown patient",
                entry.Source == QueueEntrySource.WalkIn ? "Walk-in" : "Scheduled",
                entry.Status.ToString(),
                entry.QueueSequence,
                entry.CalledAt,
                entry.ConsultationStartedAt));
        }

        return View(new ConsultationsViewModel { Entries = items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        try
        {
            await consultations.StartAsync(id, userId.Value, cancellationToken);
            TempData["StatusMessage"] = "Consultation started.";
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        Guid id,
        string? notes,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        try
        {
            await consultations.CompleteAsync(id, userId.Value, notes, cancellationToken);
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
