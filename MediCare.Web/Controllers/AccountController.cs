using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MediCare.Web.Models;

namespace MediCare.Web.Controllers;

public sealed class AccountController(SignInManager<ApplicationUser> signInManager) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Home");

        ModelState.AddModelError(string.Empty, "The email or password is incorrect.");
        return View(model);
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
