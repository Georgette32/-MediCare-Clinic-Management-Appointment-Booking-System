using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public sealed class WalkInAdmissionsController(
    IWalkInAdmissionApprovalService admissionApprovalService,
    ICurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        var pending = await admissionApprovalService.GetPendingForDoctorAsync(
            userId.Value, cancellationToken);

        return View(new WalkInAdmissionsViewModel(pending));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(
        WalkInAdmissionDecisionViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        try
        {
            TempData["StatusMessage"] = await admissionApprovalService.DecideAsync(
                model.ApprovalId,
                userId.Value,
                model.Approve,
                model.Notes,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
