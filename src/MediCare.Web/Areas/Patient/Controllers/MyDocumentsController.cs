using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Documents;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = Policies.PatientOnly)]
public class MyDocumentsController : Controller
{
    private readonly IMedicalDocumentService _documents;
    private readonly ICurrentUserService _currentUser;

    public MyDocumentsController(
        IMedicalDocumentService documents,
        ICurrentUserService currentUser)
    {
        _documents = documents;
        _currentUser = currentUser;
    }

    private Guid PatientUserId => _currentUser.UserId ?? Guid.Empty;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await _documents.GetMyDocumentsAsync(PatientUserId, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.Error ?? "Could not load your documents.";
            return View("MyDocuments", (IReadOnlyList<MedicalDocumentDto>)Array.Empty<MedicalDocumentDto>());
        }

        return View("MyDocuments", result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(7 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file.OpenReadStream();
        var result = await _documents.UploadAsPatientAsync(
            PatientUserId,
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
            TempData["Success"] = "Your document was uploaded.";
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _documents.DownloadMyDocumentAsync(PatientUserId, id, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _documents.DeleteMyDocumentAsync(PatientUserId, id, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "The document was deleted.";
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors);
        }

        return RedirectToAction(nameof(Index));
    }
}
