using System.Security.Claims;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>Patient-side medical history, active prescriptions and printable prescriptions. Own records only.</summary>
[Authorize(Roles = MediCareRoles.Patient)]
[MedicalNoStore]
public sealed class MedicalController(IMedicalRecordService medical) : Controller
{
    [HttpGet("Medical/History")]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var history = await medical.GetMyHistoryAsync(userId, cancellationToken);
        if (history is null)
            return Forbid();

        ViewData["Title"] = "My medical history";
        ViewData["PrintBase"] = "/Medical/Prescription/";
        return View(history);
    }

    [HttpGet("Medical/ActivePrescriptions")]
    public async Task<IActionResult> ActivePrescriptions(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var active = await medical.GetActivePrescriptionsAsync(userId, cancellationToken);
        return View(active);
    }

    [HttpGet("Medical/Prescription/{recordId:guid}")]
    public async Task<IActionResult> Prescription(Guid recordId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var record = await medical.GetRecordAsync(recordId, userId, MediCare.Application.Services.MedicalViewerRole.Patient, cancellationToken);
        if (record is null || record.Prescription is null)
            return NotFound();

        return View("PrescriptionPrint", record);
    }

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
