using MediCare.Application.DTOs.MedicalRecords;
using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = Policies.PatientOnly)]
public class RecordsController : Controller
{
    private readonly IMedicalRecordService _records;
    private readonly ICurrentUserService _currentUser;

    public RecordsController(
        IMedicalRecordService records,
        ICurrentUserService currentUser)
    {
        _records = records;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await _records.GetMyRecordsAsync(_currentUser.UserId ?? Guid.Empty, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.Error ?? "Could not load your medical records.";
            return View("MyRecords", (IReadOnlyList<MedicalRecordDto>)Array.Empty<MedicalRecordDto>());
        }

        return View("MyRecords", result.Value);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var result = await _records.GetMyRecordAsync(_currentUser.UserId ?? Guid.Empty, id, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        return View("RecordDetails", result.Value);
    }
}
