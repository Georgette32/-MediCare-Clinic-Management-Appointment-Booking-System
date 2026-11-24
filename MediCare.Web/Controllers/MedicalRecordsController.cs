using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Filters;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediCare.Web.Controllers;

/// <summary>
/// Doctor-side visit records, addenda, prescriptions, patient history and printable prescriptions. Every action resolves
/// the doctor from the signed-in user; the service refuses records outside the doctor's treating relationships.
/// </summary>
[Authorize(Roles = MediCareRoles.Doctor)]
[MedicalNoStore]
public sealed class MedicalRecordsController(IMedicalRecordService medical) : Controller
{
    private const int DefaultBlankRows = 5;
    private const int MaxBlankRows = 20;
    private const int MaxPrescriptionRows = 50;

    [HttpGet("Doctor/Visit/{queueEntryId:guid}")]
    public async Task<IActionResult> Visit(Guid queueEntryId, int? rows, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var model = await BuildVisitModelAsync(queueEntryId, doctorUserId, rows, posted: null, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>
    /// Saves the visit text. A validation failure or a refused save re-renders the page with the typed values and the
    /// errors (no redirect, so nothing typed is lost). A successful save redirects (PRG).
    /// </summary>
    [HttpPost("Doctor/Visit/{queueEntryId:guid}/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVisit(Guid queueEntryId, [Bind(Prefix = "Visit")] VisitRecordForm form, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        if (!ModelState.IsValid)
            return await RedisplayVisitAsync(queueEntryId, doctorUserId, form, cancellationToken);

        var result = await medical.CreateOrUpdateForVisitAsync(
            queueEntryId,
            doctorUserId,
            new VisitRecordInput(form.ChiefComplaint, form.Diagnosis, form.ClinicalNotes),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RedisplayVisitAsync(queueEntryId, doctorUserId, form, cancellationToken);
        }

        SetResult(result);
        return RedirectToAction(nameof(Visit), new { queueEntryId });
    }

    [HttpPost("Doctor/Record/{recordId:guid}/Prescription")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePrescription(Guid recordId, [Bind(Prefix = "Prescription")] PrescriptionForm form, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var queueEntryId = form.QueueEntryId;
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError() ?? "Check the prescription items and try again.";
            return RedirectToVisit(queueEntryId);
        }
        if (form.Items.Count > MaxPrescriptionRows)
        {
            TempData["ErrorMessage"] = $"A prescription can have at most {MaxPrescriptionRows} items.";
            return RedirectToVisit(queueEntryId);
        }

        var items = form.Items
            .Select(row => new PrescriptionItemInput(row.MedicationName, row.Dosage, row.Frequency, row.DurationDays, row.Instructions))
            .ToArray();
        var result = await medical.SavePrescriptionAsync(recordId, doctorUserId, items, form.Notes, cancellationToken);
        SetResult(result);
        return RedirectToVisit(queueEntryId);
    }

    [HttpPost("Doctor/Record/{recordId:guid}/Addendum")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAddendum(Guid recordId, AddendumForm form, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError() ?? "Write the addendum text and try again.";
            return RedirectToVisit(form.QueueEntryId);
        }

        var result = await medical.AddAddendumAsync(recordId, doctorUserId, form.Text, cancellationToken);
        SetResult(result);
        return RedirectToVisit(form.QueueEntryId);
    }

    [HttpGet("Doctor/Patient/{patientId:guid}/History")]
    public async Task<IActionResult> PatientHistory(Guid patientId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var history = await medical.GetPatientHistoryAsync(patientId, doctorUserId, MedicalViewerRole.Doctor, cancellationToken);
        if (history is null)
            return NotFound();

        ViewData["Title"] = "Patient history";
        ViewData["PrintBase"] = "/Doctor/Prescription/";
        return View(history);
    }

    [HttpGet("Doctor/Prescription/{recordId:guid}")]
    public async Task<IActionResult> Prescription(Guid recordId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var record = await medical.GetRecordAsync(recordId, doctorUserId, MedicalViewerRole.Doctor, cancellationToken);
        if (record is null || record.Prescription is null)
            return NotFound();

        return View("PrescriptionPrint", record);
    }

    private async Task<IActionResult> RedisplayVisitAsync(Guid queueEntryId, Guid doctorUserId, VisitRecordForm posted, CancellationToken cancellationToken)
    {
        var model = await BuildVisitModelAsync(queueEntryId, doctorUserId, rows: null, posted, cancellationToken);
        return model is null ? NotFound() : View("Visit", model);
    }

    /// <summary>Builds the visit page. The posted form, when given, replaces the stored text so typed values survive a failed save.</summary>
    private async Task<VisitPageModel?> BuildVisitModelAsync(Guid queueEntryId, Guid doctorUserId, int? rows, VisitRecordForm? posted, CancellationToken cancellationToken)
    {
        var page = await medical.GetVisitForDoctorAsync(queueEntryId, doctorUserId, cancellationToken);
        if (page is null)
            return null;

        var existing = page.Record?.Prescription?.Items ?? [];
        var prescription = new PrescriptionForm
        {
            QueueEntryId = queueEntryId,
            Notes = page.Record?.Prescription?.Notes,
            Items = existing.Select(item => new PrescriptionRowForm
            {
                MedicationName = item.MedicationName,
                Dosage = item.Dosage,
                Frequency = item.Frequency,
                DurationDays = item.DurationDays,
                Instructions = item.Instructions
            }).ToList()
        };
        var blankRows = Math.Clamp(rows ?? DefaultBlankRows, 1, MaxBlankRows);
        prescription.Items.AddRange(Enumerable.Range(0, blankRows).Select(_ => new PrescriptionRowForm()));

        return new VisitPageModel
        {
            Page = page,
            Visit = posted ?? new VisitRecordForm
            {
                ChiefComplaint = page.Record?.ChiefComplaint,
                Diagnosis = page.Record?.Diagnosis ?? string.Empty,
                ClinicalNotes = page.Record?.ClinicalNotes
            },
            Prescription = prescription,
            BlankRows = blankRows
        };
    }

    private IActionResult RedirectToVisit(Guid queueEntryId) =>
        queueEntryId == Guid.Empty
            ? RedirectToAction(nameof(DoctorController.Queue), "Doctor")
            : RedirectToAction(nameof(Visit), new { queueEntryId });

    private void SetResult(MedicalActionResult result)
    {
        if (result.Succeeded)
            TempData["StatusMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
    }

    private string? FirstModelError() =>
        ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault();

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
