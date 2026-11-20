using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Patients;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Patient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = Policies.PatientOnly)]
public class ProfileController : Controller
{
    private readonly IPatientProfileService _profiles;
    private readonly ICurrentUserService _currentUser;

    public ProfileController(
        IPatientProfileService profiles,
        ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Edit(CancellationToken cancellationToken)
    {
        var result = await _profiles.GetMyProfileAsync(_currentUser.UserId ?? Guid.Empty, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        return View(ToViewModel(result.Value));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PatientProfileEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _profiles.UpdateMyProfileAsync(
            _currentUser.UserId ?? Guid.Empty,
            new UpdatePatientProfileRequest
            {
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                DateOfBirth = model.DateOfBirth,
                Gender = model.Gender,
                Address = model.Address,
                EmergencyContact = model.EmergencyContact,
                BloodType = model.BloodType
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Your profile was updated.";
        return RedirectToAction(nameof(Edit));
    }

    private static PatientProfileEditViewModel ToViewModel(PatientProfileDto profile) => new()
    {
        Email = profile.Email,
        FullName = profile.FullName,
        PhoneNumber = profile.PhoneNumber,
        DateOfBirth = profile.DateOfBirth,
        Gender = profile.Gender,
        Address = profile.Address,
        EmergencyContact = profile.EmergencyContact,
        BloodType = profile.BloodType
    };

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
