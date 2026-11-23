using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Services;
using MediCare.Domain.Enums;
using MediCare.Web.ViewModels.Patient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = Policies.PatientOnly)]
public sealed class AppointmentsController(
    AppointmentService appointmentService,
    IPatientProfileService patientProfiles,
    IDoctorManagementService doctors,
    ICurrentUserService currentUser,
    IClinicClock clinicClock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Book(CancellationToken cancellationToken)
    {
        var model = new BookAppointmentViewModel();
        if (!await TryLoadPatientProfileAsync(model, cancellationToken))
        {
            return RedirectToAction("Edit", "Profile", new { area = "Patient" });
        }

        return View(await PrepareAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(
        BookAppointmentViewModel model,
        CancellationToken cancellationToken)
    {
        if (!await TryLoadPatientProfileAsync(model, cancellationToken))
        {
            return RedirectToAction("Edit", "Profile", new { area = "Patient" });
        }

        if (model.Date < clinicClock.Today)
        {
            ModelState.AddModelError(nameof(model.Date), "Choose today or a future date.");
        }

        if (model.Date == clinicClock.Today && model.StartTime <= clinicClock.CurrentTime)
        {
            ModelState.AddModelError(nameof(model.StartTime), "Choose a time later today.");
        }

        if (!ModelState.IsValid)
        {
            return View(await PrepareAsync(model, cancellationToken));
        }

        try
        {
            await appointmentService.RequestAsync(
                model.PatientProfileId,
                model.DoctorProfileId,
                model.Date,
                model.StartTime,
                cancellationToken);

            TempData["Success"] = "Your appointment request was submitted for review.";
            return RedirectToAction("Index", "Dashboard", new { area = "Patient" });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(await PrepareAsync(model, cancellationToken));
        }
    }

    private async Task<bool> TryLoadPatientProfileAsync(
        BookAppointmentViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return false;
        }

        var profile = await patientProfiles.GetMyProfileAsync(userId.Value, cancellationToken);
        if (!profile.Succeeded || profile.Value is null)
        {
            TempData["Error"] = profile.Error ?? "Complete your health profile before booking.";
            return false;
        }

        model.PatientProfileId = profile.Value.Id;
        return true;
    }

    private async Task<BookAppointmentViewModel> PrepareAsync(
        BookAppointmentViewModel model,
        CancellationToken cancellationToken)
    {
        var doctorList = await doctors.GetDoctorsAsync(
            new DoctorQuery
            {
                ApprovalStatus = DoctorApprovalStatus.Approved,
                IsActive = true,
                Page = 1,
                PageSize = 100
            },
            cancellationToken);

        model.Doctors = doctorList.Items
            .Select(doctor => new DoctorOption(doctor.Id, doctor.FullName, doctor.Specialization))
            .ToArray();

        model.TimeSlots = Enumerable.Range(0, 17)
            .Select(index => new TimeOnly(9, 0).Add(TimeSpan.FromMinutes(30 * index)))
            .ToArray();

        return model;
    }
}
