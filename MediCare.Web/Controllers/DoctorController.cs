using System.Security.Claims;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = MediCareRoles.Doctor)]
public sealed class DoctorController(IWalkInAdmissionApprovalService admissionApprovalService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> WalkInAdmissions(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var pending = await admissionApprovalService.GetPendingForDoctorAsync(doctorUserId, cancellationToken);
        return View(new WalkInAdmissionsViewModel(pending));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideWalkInAdmission(WalkInAdmissionDecisionViewModel model, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        try
        {
            TempData["StatusMessage"] = await admissionApprovalService.DecideAsync(
                model.ApprovalId, doctorUserId, model.Approve, model.Notes, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(WalkInAdmissions));
    }

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
