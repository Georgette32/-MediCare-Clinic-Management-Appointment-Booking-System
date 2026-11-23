using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Documents;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public class DocumentsController : Controller
{
    private readonly IMedicalDocumentService _documents;
    private readonly IMedicalRecordService _records;
    private readonly ICurrentUserService _currentUser;

    public DocumentsController(
        IMedicalDocumentService documents,
        IMedicalRecordService records,
        ICurrentUserService currentUser)
    {
        _documents = documents;
        _records = records;
        _currentUser = currentUser;
    }

    private Guid DoctorUserId => _currentUser.UserId ?? Guid.Empty;

    // All documents of one patient, plus the upload form.
    public async Task<IActionResult> ForPatient(Guid id, CancellationToken cancellationToken)
    {
        var documents = await _documents.GetPatientDocumentsForDoctorAsync(DoctorUserId, id, cancellationToken);
        if (!documents.Succeeded || documents.Value is null)
        {
            TempData["Error"] = documents.Error ?? "Could not load the documents.";
            return RedirectToAction("Index", "MedicalRecords");
        }

        var patients = await _records.GetMyPatientsAsync(DoctorUserId, cancellationToken);
        var patientName = patients.Value?.FirstOrDefault(p => p.PatientProfileId == id)?.FullName ?? "Patient";

        return View("PatientDocuments", new DoctorPatientDocumentsViewModel
        {
            PatientProfileId = id,
            PatientName = patientName,
            Documents = documents.Value
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(7 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid patientId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToAction(nameof(ForPatient), new { id = patientId });
        }

        await using var stream = file.OpenReadStream();
        var result = await _documents.UploadAsDoctorAsync(
            DoctorUserId,
            patientId,
            new UploadMedicalDocumentRequest
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeInBytes = file.Length,
                Content = stream
            },
            cancellationToken);

        if (result.Succeeded)
        {
            TempData["Success"] = "The document was uploaded.";
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors);
        }

        return RedirectToAction(nameof(ForPatient), new { id = patientId });
    }

    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _documents.DownloadAsDoctorAsync(DoctorUserId, id, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, Guid patientId, CancellationToken cancellationToken)
    {
        var result = await _documents.DeleteAsDoctorAsync(DoctorUserId, id, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "The document was deleted.";
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors);
        }

        return RedirectToAction(nameof(ForPatient), new { id = patientId });
    }
}
