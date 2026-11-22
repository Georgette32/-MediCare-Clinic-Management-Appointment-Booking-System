using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Prescriptions;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = Policies.PatientOnly)]
public class MyPrescriptionsController : Controller
{
    private readonly IPrescriptionService _prescriptions;
    private readonly ICurrentUserService _currentUser;

    public MyPrescriptionsController(
        IPrescriptionService prescriptions,
        ICurrentUserService currentUser)
    {
        _prescriptions = prescriptions;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await _prescriptions.GetMyPrescriptionsAsync(_currentUser.UserId ?? Guid.Empty, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.Error ?? "Could not load your prescriptions.";
            return View("MyPrescriptionsList", (IReadOnlyList<PrescriptionDto>)Array.Empty<PrescriptionDto>());
        }

        return View("MyPrescriptionsList", result.Value);
    }
}
