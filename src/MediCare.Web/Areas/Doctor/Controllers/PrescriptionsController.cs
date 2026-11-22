using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Prescriptions;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public class PrescriptionsController : Controller
{
    // The form always shows this many medication rows; empty rows are ignored.
    private const int ItemRows = 5;

    private readonly IPrescriptionService _prescriptions;
    private readonly IMedicalRecordService _records;
    private readonly ICurrentUserService _currentUser;

    public PrescriptionsController(
        IPrescriptionService prescriptions,
        IMedicalRecordService records,
        ICurrentUserService currentUser)
    {
        _prescriptions = prescriptions;
        _records = records;
        _currentUser = currentUser;
    }

    private Guid DoctorUserId => _currentUser.UserId ?? Guid.Empty;

    // All prescriptions of one patient.
    public async Task<IActionResult> ForPatient(Guid id, CancellationToken cancellationToken)
    {
        var prescriptions = await _prescriptions.GetPatientPrescriptionsForDoctorAsync(DoctorUserId, id, cancellationToken);
        if (!prescriptions.Succeeded || prescriptions.Value is null)
        {
            TempData["Error"] = prescriptions.Error ?? "Could not load the prescriptions.";
            return RedirectToAction("Index", "MedicalRecords");
        }

        var patients = await _records.GetMyPatientsAsync(DoctorUserId, cancellationToken);
        var patientName = patients.Value?.FirstOrDefault(p => p.PatientProfileId == id)?.FullName ?? "Patient";

        return View("PatientPrescriptions", new DoctorPatientPrescriptionsViewModel
        {
            PatientProfileId = id,
            PatientName = patientName,
            Prescriptions = prescriptions.Value
        });
    }

    [HttpGet]
    public IActionResult Create(Guid patientId, Guid? medicalRecordId)
    {
        if (patientId == Guid.Empty)
        {
            return RedirectToAction("Index", "MedicalRecords");
        }

        var model = new PrescriptionFormViewModel
        {
            PatientProfileId = patientId,
            MedicalRecordId = medicalRecordId
        };

        EnsureRows(model);
        return View("PrescriptionForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PrescriptionFormViewModel model, CancellationToken cancellationToken)
    {
        EnsureRows(model);

        if (!ModelState.IsValid)
        {
            return View("PrescriptionForm", model);
        }

        var request = new CreatePrescriptionRequest
        {
            PatientProfileId = model.PatientProfileId,
            MedicalRecordId = model.MedicalRecordId,
            Notes = model.Notes,
            Items = model.Items
                .Where(item => !IsBlank(item))
                .Select(item => new PrescriptionItemRequest
                {
                    MedicationName = item.MedicationName ?? string.Empty,
                    Dosage = item.Dosage ?? string.Empty,
                    Frequency = item.Frequency ?? string.Empty,
                    DurationInDays = item.DurationInDays ?? 0,
                    Instructions = item.Instructions
                })
                .ToList()
        };

        var result = await _prescriptions.CreateAsync(DoctorUserId, request, cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View("PrescriptionForm", model);
        }

        TempData["Success"] = "The prescription was saved.";
        return RedirectToAction(nameof(ForPatient), new { id = model.PatientProfileId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, Guid patientId, CancellationToken cancellationToken)
    {
        var result = await _prescriptions.DeleteAsync(DoctorUserId, id, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "The prescription was deleted.";
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors);
        }

        return RedirectToAction(nameof(ForPatient), new { id = patientId });
    }

    private static void EnsureRows(PrescriptionFormViewModel model)
    {
        while (model.Items.Count < ItemRows)
        {
            model.Items.Add(new PrescriptionItemInput());
        }
    }

    private static bool IsBlank(PrescriptionItemInput item) =>
        string.IsNullOrWhiteSpace(item.MedicationName) &&
        string.IsNullOrWhiteSpace(item.Dosage) &&
        string.IsNullOrWhiteSpace(item.Frequency) &&
        string.IsNullOrWhiteSpace(item.Instructions) &&
        item.DurationInDays is null;

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
