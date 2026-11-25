using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MediCare.Web.Models;
using MediCare.Web.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCare.Web.Controllers;

public sealed class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    MediCare.Application.Services.IAccountProvisioningService accountProvisioningService,
    IAccountProfileService profileService) : Controller
{
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var profile = await profileService.GetAsync(userId, cancellationToken);
        if (profile is null)
            return Challenge();

        return View(new ProfilePageModel
        {
            Profile = profile,
            NameForm = new ProfileNameForm { FullName = profile.FullName },
            EmailForm = new ProfileEmailForm { Email = profile.Email }
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProfileName(ProfileNameForm form, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var result = ModelState.IsValid
            ? await profileService.ChangeFullNameAsync(userId, form.FullName, cancellationToken)
            : new AccountProfileResult(false, "Enter a name of up to 200 characters.");
        SetProfileResult(result);

        // The top bar shows the name from the sign-in cookie; reissue it so the new name appears straight away.
        if (result.Succeeded && await signInManager.UserManager.FindByIdAsync(userId.ToString()) is { } user)
            await signInManager.RefreshSignInAsync(user);
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProfileEmail(ProfileEmailForm form, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var result = ModelState.IsValid
            ? await profileService.ChangeEmailAsync(userId, form.Email, cancellationToken)
            : new AccountProfileResult(false, "Enter a valid email address.");
        SetProfileResult(result);
        return RedirectToAction(nameof(Profile));
    }

    public static string LoginFailureMessage(string? userName) =>
        InputChecks.LooksLikePhone((userName ?? string.Empty).Trim())
            ? "The mobile number or password is incorrect. Check the number you registered with and that Caps Lock is off. After 5 wrong tries, sign-in pauses for 15 minutes."
            : "The username or password is incorrect. Staff sign in with their work email; check it and that Caps Lock is off. After 5 wrong tries, sign-in pauses for 15 minutes.";

    private void SetProfileResult(AccountProfileResult result) =>
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Message;

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register() => View(new PatientRegistrationViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<IActionResult> Register(PatientRegistrationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await accountProvisioningService.RegisterPatientAsync(
            new MediCare.Application.Services.RegisterPatientAccountRequest(model.PhoneNumber, model.FullName, model.Email, model.Password),
            cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        TempData["StatusMessage"] = "Registration complete. You can now sign in with your phone number and password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        // Field-level checks first, so a typo gets a message about that field instead of "incorrect password".
        if (InputChecks.LoginUserNameProblem(model.UserName) is string userNameProblem)
        {
            ModelState.Remove(nameof(LoginViewModel.UserName));
            ModelState.AddModelError(nameof(LoginViewModel.UserName), userNameProblem);
        }
        if (InputChecks.LoginPasswordProblem(model.Password) is string passwordProblem)
        {
            ModelState.Remove(nameof(LoginViewModel.Password));
            ModelState.AddModelError(nameof(LoginViewModel.Password), passwordProblem);
        }
        if (!ModelState.IsValid)
            return View(model);

        var userName = LoginUserName.Resolve(model.UserName);
        var result = await signInManager.PasswordSignInAsync(userName, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Deactivated accounts get the same generic message as a wrong password, so account state is not revealed.
            var user = await userManager.FindByNameAsync(userName);
            if (AccountAccessPolicy.IsSessionAllowed(user is not null, user?.IsActive ?? false))
                return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Home");

            await signInManager.SignOutAsync();
        }

        // One message for a wrong password, an unknown account, a deactivated account and a lockout, so the page never
        // reveals which accounts exist; it still says what to check.
        ModelState.AddModelError(string.Empty, LoginFailureMessage(model.UserName));
        return View(model);
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        // Identity persists this flag with the password hash in its single user update.
        var previousMustChangePassword = user.MustChangePassword;
        user.MustChangePassword = false;
        var changed = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!changed.Succeeded)
        {
            user.MustChangePassword = previousMustChangePassword;
            foreach (var error in changed.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        // The security stamp changed with the password; refresh the cookie so the user stays signed in.
        await signInManager.RefreshSignInAsync(user);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
