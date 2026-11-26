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
    IAccountProfileService profileService,
    IEmailVerificationService emailVerification,
    TimeProvider time,
    IWebHostEnvironment environment,
    Microsoft.Extensions.Options.IOptions<MediCare.Infrastructure.Email.EmailOptions> emailOptions) : Controller
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

        // Signed in straight away; the session middleware keeps the account on the verify page until the code is entered.
        var user = await userManager.FindByNameAsync(result.CanonicalPhone!);
        if (user is null)
            return RedirectToAction(nameof(Login));
        await signInManager.SignInAsync(user, isPersistent: false);
        var sent = await emailVerification.SendCodeAsync(user, SiteUrl, cancellationToken);
        if (sent.Status == EmailCodeSendStatus.Failed)
            TempData["VerifySendFailed"] = true;
        return RedirectToAction(nameof(VerifyEmail));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> VerifyEmail(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();
        if (!EmailCodeRules.IsPending(user))
            return RedirectToAction("Index", "Home");

        // Arriving with no usable code (expired, used up, or signing in again later) sends a fresh one, unless one was
        // sent less than a minute ago; reloading the page therefore never floods the inbox.
        var sendFailed = TempData["VerifySendFailed"] is true;
        var state = emailVerification.GetState(user);
        if (state.ExpiresAtUtc is null && state.CooldownSeconds == 0)
        {
            var sent = await emailVerification.SendCodeAsync(user, SiteUrl, cancellationToken);
            sendFailed = sent.Status == EmailCodeSendStatus.Failed;
            state = emailVerification.GetState(user);
        }

        ViewData["PublicChrome"] = true;
        // Local development without a Gmail App Password writes the code to the console instead of sending it; say so
        // on the page, so nobody waits for an email that will never arrive. Never shown outside Development.
        ViewData["CodeInConsole"] = environment.IsDevelopment() && !emailOptions.Value.CanSend;
        return View(new VerifyEmailViewModel
        {
            FirstName = MediCare.Infrastructure.Email.VerificationEmail.FirstName(user.FullName),
            MaskedEmail = state.MaskedEmail,
            ExpiresAtUnixMs = state.ExpiresAtUtc?.ToUnixTimeMilliseconds(),
            ServerNowUnixMs = time.GetUtcNow().ToUnixTimeMilliseconds(),
            LifetimeSeconds = (int)EmailCodeRules.Lifetime.TotalSeconds,
            CooldownSeconds = state.CooldownSeconds,
            TriesLeft = state.TriesLeft,
            SendFailed = sendFailed
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> VerifyEmail(string[]? code)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        // The script posts the whole code once; without JavaScript each of the six boxes posts its own digit.
        var result = await emailVerification.VerifyAsync(user, string.Concat(code ?? []));
        var home = Url.Action("Index", "Home") ?? "/";
        if (!IsFetch)
        {
            if (result.Status is not (EmailCodeCheckStatus.Confirmed or EmailCodeCheckStatus.NotPending))
                TempData["VerifyMessage"] = VerifyMessage(result);
            return result.Status is EmailCodeCheckStatus.Confirmed or EmailCodeCheckStatus.NotPending
                ? Redirect(home)
                : RedirectToAction(nameof(VerifyEmail));
        }

        return result.Status switch
        {
            EmailCodeCheckStatus.Confirmed or EmailCodeCheckStatus.NotPending => Json(new { status = "ok", redirect = home }),
            EmailCodeCheckStatus.Wrong => Json(new { status = "wrong", triesLeft = result.TriesLeft, message = VerifyMessage(result) }),
            EmailCodeCheckStatus.Expired => Json(State(user, "expired", VerifyMessage(result))),
            _ => Json(State(user, "locked", VerifyMessage(result)))
        };
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> ResendCode(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var sent = await emailVerification.SendCodeAsync(user, SiteUrl, cancellationToken);
        var (status, message) = sent.Status switch
        {
            EmailCodeSendStatus.Sent => ("sent", "A new code is on its way."),
            EmailCodeSendStatus.CoolingDown => ("cooldown", $"Wait {sent.CooldownSeconds} seconds before asking for another code."),
            EmailCodeSendStatus.NotPending => ("ok", null),
            _ => ("failed", "We couldn't send the email just now. Try again in a moment.")
        };
        if (!IsFetch)
        {
            if (message is not null)
                TempData["VerifyMessage"] = message;
            return RedirectToAction(nameof(VerifyEmail));
        }
        return Json(State(user, status, message));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> ChangeEmail(string? email, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var changed = await emailVerification.ChangeEmailAsync(user, email, SiteUrl, cancellationToken);
        var message = !changed.Succeeded
            ? changed.Error
            : changed.Send?.Status == EmailCodeSendStatus.Sent
                ? "Address updated. We sent a new code to it."
                : "Address updated, but we couldn't send the email just now. Press Resend in a moment.";
        if (!IsFetch)
        {
            TempData["VerifyMessage"] = message;
            return RedirectToAction(nameof(VerifyEmail));
        }
        return Json(State(user, changed.Succeeded ? "changed" : "error", message));
    }

    private bool IsFetch => string.Equals(Request.Headers["X-Requested-With"], "fetch", StringComparison.Ordinal);

    private string SiteUrl => $"{Request.Scheme}://{Request.Host}";

    private object State(ApplicationUser user, string status, string? message)
    {
        var state = emailVerification.GetState(user);
        return new
        {
            status,
            message,
            maskedEmail = state.MaskedEmail,
            expiresAt = state.ExpiresAtUtc?.ToUnixTimeMilliseconds(),
            serverNow = time.GetUtcNow().ToUnixTimeMilliseconds(),
            cooldown = state.CooldownSeconds,
            triesLeft = state.TriesLeft
        };
    }

    private static string VerifyMessage(EmailCodeCheck result) => result.Status switch
    {
        EmailCodeCheckStatus.Wrong => $"That code doesn't match. {result.TriesLeft} {(result.TriesLeft == 1 ? "try" : "tries")} left.",
        EmailCodeCheckStatus.Expired => "This code has expired. Send a new one to continue.",
        EmailCodeCheckStatus.Locked => "Too many wrong tries. Send a new code to continue.",
        _ => string.Empty
    };

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
