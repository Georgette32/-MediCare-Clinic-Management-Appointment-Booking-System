using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.MedicalRecords;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public class MedicalRecordsController : Controller
{
    private readonly IMedicalRecordService _records;
    private readonly ICurrentUserService _currentUser;

    public MedicalRecordsController(
        IMedicalRecordService records,
        ICurrentUserService currentUser)
    {
        _records = records;
        _currentUser = currentUser;
    }

    private Guid DoctorUserId => _currentUser.UserId ?? Guid.Empty;

    // The doctor's patients (those with a completed appointment).
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await _records.GetMyPatientsAsync(DoctorUserId, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.Error ?? "Could not load your patients.";
            return RedirectToAction("Index", "Dashboard");
        }

        return View("MyPatients", result.Value);
    }

    // All records of one patient.
    public async Task<IActionResult> Records(Guid id, CancellationToken cancellationToken)
    {
        var records = await _records.GetPatientRecordsForDoctorAsync(DoctorUserId, id, cancellationToken);
        if (!records.Succeeded || records.Value is null)
        {
            TempData["Error"] = records.Error ?? "Could not load the records.";
            return RedirectToAction(nameof(Index));
        }

        var patients = await _records.GetMyPatientsAsync(DoctorUserId, cancellationToken);
        var patientName = patients.Value?.FirstOrDefault(p => p.PatientProfileId == id)?.FullName ?? "Patient";

        return View("PatientRecords", new DoctorPatientRecordsViewModel
        {
            PatientProfileId = id,
            PatientName = patientName,
            Records = records.Value
        });
    }

    [HttpGet]
    public IActionResult Create(Guid patientId, Guid? appointmentId)
    {
        if (patientId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        return View("RecordForm", new MedicalRecordFormViewModel
        {
            PatientProfileId = patientId,
            AppointmentId = appointmentId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MedicalRecordFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("RecordForm", model);
        }

        var result = await _records.CreateAsync(
            DoctorUserId,
            new CreateMedicalRecordRequest
            {
                PatientProfileId = model.PatientProfileId,
                AppointmentId = model.AppointmentId,
                Symptoms = model.Symptoms,
                Diagnosis = model.Diagnosis,
                TreatmentPlan = model.TreatmentPlan,
                DoctorNotes = model.DoctorNotes
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View("RecordForm", model);
        }

        TempData["Success"] = "The medical record was saved.";
        return RedirectToAction(nameof(Records), new { id = model.PatientProfileId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid patientId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _records.GetPatientRecordsForDoctorAsync(DoctorUserId, patientId, cancellationToken);
        var record = result.Value?.FirstOrDefault(r => r.Id == id);
        if (!result.Succeeded || record is null)
        {
            return NotFound();
        }

        return View("RecordForm", new MedicalRecordFormViewModel
        {
            RecordId = record.Id,
            PatientProfileId = record.PatientProfileId,
            AppointmentId = record.AppointmentId,
            Symptoms = record.Symptoms,
            Diagnosis = record.Diagnosis,
            TreatmentPlan = record.TreatmentPlan,
            DoctorNotes = record.DoctorNotes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MedicalRecordFormViewModel model, CancellationToken cancellationToken)
    {
        if (model.RecordId is null)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View("RecordForm", model);
        }

        var result = await _records.UpdateAsync(
            DoctorUserId,
            new UpdateMedicalRecordRequest
            {
                RecordId = model.RecordId.Value,
                Symptoms = model.Symptoms,
                Diagnosis = model.Diagnosis,
                TreatmentPlan = model.TreatmentPlan,
                DoctorNotes = model.DoctorNotes
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View("RecordForm", model);
        }

        TempData["Success"] = "The medical record was updated.";
        return RedirectToAction(nameof(Records), new { id = model.PatientProfileId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, Guid patientId, CancellationToken cancellationToken)
    {
        var result = await _records.DeleteAsync(DoctorUserId, id, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "The medical record was deleted.";
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors);
        }

        return RedirectToAction(nameof(Records), new { id = patientId });
    }

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
