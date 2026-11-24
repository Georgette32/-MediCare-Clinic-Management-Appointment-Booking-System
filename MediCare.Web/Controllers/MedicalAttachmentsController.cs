using System.Security.Claims;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Filters;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MediCare.Web.Controllers;

/// <summary>
/// Medical attachments. Upload is limited to doctors with a treating relationship (the service re-checks it). Download is
/// open to the doctor or patient the service authorizes; files are streamed from outside wwwroot and never served as static
/// content. There is no delete: attachments are append-only.
/// </summary>
[MedicalNoStore]
public sealed class MedicalAttachmentsController(IMedicalAttachmentService attachments) : Controller
{
    [HttpPost("Doctor/Record/{recordId:guid}/Attachments")]
    [Authorize(Roles = MediCareRoles.Doctor)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MedicalAttachmentLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MedicalAttachmentLimits.MaxRequestBytes)]
    public async Task<IActionResult> Upload(
        Guid recordId,
        IFormFile? file,
        string? description,
        Guid? queueEntryId,
        Guid? patientId,
        CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Choose a PDF, JPEG or PNG file to attach.";
            return RedirectBack(queueEntryId, patientId);
        }

        await using var content = file.OpenReadStream();
        var result = await attachments.UploadAsync(
            recordId,
            doctorUserId,
            new MedicalFileUpload(content, file.FileName, description),
            cancellationToken);

        if (result.Succeeded)
            TempData["StatusMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
        return RedirectBack(queueEntryId, patientId);
    }

    [HttpGet("Medical/Attachments/{attachmentId:guid}")]
    [Authorize(Roles = MediCareRoles.Doctor + "," + MediCareRoles.Patient)]
    public async Task<IActionResult> Download(Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var role = User.IsInRole(MediCareRoles.Doctor) ? MedicalViewerRole.Doctor : MedicalViewerRole.Patient;
        MedicalAttachmentDownload? download;
        try
        {
            download = await attachments.GetForDownloadAsync(attachmentId, userId, role, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // The metadata exists but the bytes are missing on disk: report it as not found rather than a server error.
            return NotFound();
        }

        if (download is null)
            return NotFound();

        Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("attachment") { FileNameStar = download.FileName }.ToString();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        return File(download.Content, download.ContentType, enableRangeProcessing: false);
    }

    private IActionResult RedirectBack(Guid? queueEntryId, Guid? patientId)
    {
        if (queueEntryId is Guid entryId && entryId != Guid.Empty)
            return RedirectToAction(nameof(MedicalRecordsController.Visit), "MedicalRecords", new { queueEntryId = entryId });
        if (patientId is Guid historyPatientId && historyPatientId != Guid.Empty)
            return RedirectToAction(nameof(MedicalRecordsController.PatientHistory), "MedicalRecords", new { patientId = historyPatientId });
        return RedirectToAction(nameof(DoctorController.Queue), "Doctor");
    }

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
