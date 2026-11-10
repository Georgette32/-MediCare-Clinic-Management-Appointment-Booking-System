using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Auth;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Enums;
using MediCare.Web.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MediCare.Web.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly IIdentityService _identity;
    private readonly ISpecializationService _specializations;
    private readonly IDoctorManagementService _doctors;
    private readonly ICurrentUserService _currentUser;

    public AccountController(
        IAuthService auth,
        IIdentityService identity,
        ISpecializationService specializations,
        IDoctorManagementService doctors,
        ICurrentUserService currentUser)
    {
        _auth = auth;
        _identity = identity;
        _specializations = specializations;
        _doctors = doctors;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult RegisterPatient()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }

        return View(new RegisterPatientViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterPatient(RegisterPatientViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _auth.RegisterPatientAsync(new RegisterPatientRequest
        {
            FullName = model.FullName,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword,
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender,
            Address = model.Address
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = _auth.RequiresEmailConfirmation
            ? "Your account was created. Check your email to confirm your address before signing in."
            : "Your account was created. You can now sign in.";

        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> RegisterDoctor(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }

        await LoadSpecializationsAsync(cancellationToken);
        return View(new RegisterDoctorViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterDoctor(RegisterDoctorViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadSpecializationsAsync(cancellationToken);
            return View(model);
        }

        var result = await _auth.RegisterDoctorAsync(new RegisterDoctorRequest
        {
            FullName = model.FullName,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword,
            SpecializationId = model.SpecializationId,
            LicenseNumber = model.LicenseNumber,
            ConsultationFee = model.ConsultationFee,
            YearsOfExperience = model.YearsOfExperience,
            Bio = model.Bio
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            await LoadSpecializationsAsync(cancellationToken);
            return View(model);
        }

        TempData["Success"] = _auth.RequiresEmailConfirmation
            ? "Your doctor application was received. Confirm your email address; an administrator will review your application afterwards."
            : "Your doctor application was received. An administrator will review it before you can access the doctor dashboard.";

        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null, string? reason = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }

        if (string.Equals(reason, "inactive", StringComparison.OrdinalIgnoreCase))
        {
            ViewData["Message"] = "Your account has been deactivated. Contact the clinic administration.";
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var outcome = await _auth.LoginAsync(new LoginRequest
        {
            Email = model.Email,
            Password = model.Password,
            RememberMe = model.RememberMe
        }, cancellationToken);

        switch (outcome)
        {
            case SignInOutcome.Success:
                break;
            case SignInOutcome.EmailNotConfirmed:
                ModelState.AddModelError(string.Empty, "Confirm your email address before signing in.");
                return View(model);
            case SignInOutcome.Inactive:
                ModelState.AddModelError(string.Empty, "This account is deactivated. Contact the clinic administration.");
                return View(model);
            case SignInOutcome.LockedOut:
                ModelState.AddModelError(string.Empty, "The account is temporarily locked after several failed attempts. Try again later.");
                return View(model);
            default:
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(model);
        }

        var user = await _identity.FindByEmailAsync(model.Email.Trim(), cancellationToken);
        if (user is not null)
        {
            var roles = await _identity.GetRolesAsync(user.Id, cancellationToken);

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            if (roles.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
            }

            if (roles.Contains(Roles.Doctor, StringComparer.OrdinalIgnoreCase))
            {
                var profile = await _doctors.GetDoctorByUserIdAsync(user.Id, cancellationToken);
                if (profile.Succeeded && profile.Value?.ApprovalStatus == DoctorApprovalStatus.Approved)
                {
                    return RedirectToAction("Index", "Dashboard", new { area = "Doctor" });
                }

                return RedirectToAction(nameof(PendingApproval));
            }

            if (roles.Contains(Roles.Patient, StringComparer.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Dashboard", new { area = "Patient" });
            }
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _auth.LogoutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _auth.ForgotPasswordAsync(
            new ForgotPasswordRequest { Email = model.Email },
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ResetPassword(Guid userId, string token, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction(nameof(Login));
        }

        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            TempData["Error"] = "The reset link is invalid or has expired.";
            return RedirectToAction(nameof(Login));
        }

        return View(new ResetPasswordViewModel { Email = user.Email, Token = token });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _auth.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = model.Email,
            Token = model.Token,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Your password was reset. You can now sign in.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(Guid userId, string token, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return View(new ConfirmEmailViewModel { Succeeded = false, Message = "The confirmation link is invalid." });
        }

        var result = await _auth.ConfirmEmailAsync(userId, token, cancellationToken);
        return View(new ConfirmEmailViewModel
        {
            Succeeded = result.Succeeded,
            Message = result.Succeeded
                ? "Your email address was confirmed. You can now sign in."
                : result.Error ?? "The confirmation link is invalid or has expired."
        });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _auth.GetProfileAsync(userId.Value, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        return View(ToProfileViewModel(result.Value));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            await PopulateProfileMetadataAsync(userId.Value, model, cancellationToken);
            return View(model);
        }

        var result = await _auth.UpdateProfileAsync(userId.Value, new UpdateProfileRequest
        {
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber,
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender,
            Address = model.Address,
            EmergencyContact = model.EmergencyContact,
            BloodType = model.BloodType
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            await PopulateProfileMetadataAsync(userId.Value, model, cancellationToken);
            return View(model);
        }

        TempData["Success"] = "Your profile was updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _auth.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = model.CurrentPassword,
            NewPassword = model.NewPassword,
            ConfirmPassword = model.ConfirmPassword
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Your password was changed.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize(Roles = Roles.Doctor)]
    [HttpGet]
    public async Task<IActionResult> PendingApproval(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _doctors.GetDoctorByUserIdAsync(userId.Value, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        if (result.Value.ApprovalStatus == DoctorApprovalStatus.Approved)
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Doctor" });
        }

        return View(result.Value);
    }

    private IActionResult RedirectToDashboard()
    {
        if (User.IsInRole(Roles.Admin))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        if (User.IsInRole(Roles.Doctor))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Doctor" });
        }

        if (User.IsInRole(Roles.Patient))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Patient" });
        }

        return RedirectToAction("Index", "Home");
    }

    private async Task LoadSpecializationsAsync(CancellationToken cancellationToken)
    {
        var specializations = await _specializations.GetActiveAsync(cancellationToken);
        ViewBag.Specializations = new SelectList(specializations, "Id", "Name");
    }

    private void AddErrors(MediCare.Application.Common.Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private static ProfileViewModel ToProfileViewModel(ProfileDto dto) => new()
    {
        FullName = dto.FullName,
        Email = dto.Email,
        PhoneNumber = dto.PhoneNumber ?? string.Empty,
        DateOfBirth = dto.DateOfBirth,
        Gender = dto.Gender,
        Address = dto.Address,
        EmergencyContact = dto.EmergencyContact,
        BloodType = dto.BloodType,
        IsPatient = dto.Roles.Contains(Roles.Patient, StringComparer.OrdinalIgnoreCase),
        IsActive = dto.IsActive,
        EmailConfirmed = dto.EmailConfirmed,
        Roles = dto.Roles
    };

    private async Task PopulateProfileMetadataAsync(Guid userId, ProfileViewModel model, CancellationToken cancellationToken)
    {
        var profile = await _auth.GetProfileAsync(userId, cancellationToken);
        if (profile.Succeeded && profile.Value is not null)
        {
            model.Email = profile.Value.Email;
            model.IsActive = profile.Value.IsActive;
            model.EmailConfirmed = profile.Value.EmailConfirmed;
            model.Roles = profile.Value.Roles;
            model.IsPatient = profile.Value.Roles.Contains(Roles.Patient, StringComparer.OrdinalIgnoreCase);
        }
    }
}
