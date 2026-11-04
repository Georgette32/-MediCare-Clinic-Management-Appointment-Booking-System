using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

public sealed class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : Controller
{
    [HttpGet]
    public IActionResult Register() => View(new RegisterInput());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = new IdentityUser { UserName = input.Email, Email = input.Email };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) { foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description); return View(input); }
        var role = await users.AddToRoleAsync(user, "Patient");
        if (!role.Succeeded) { await users.DeleteAsync(user); ModelState.AddModelError(string.Empty, "Patient role is not initialized."); return View(input); }
        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Appointments");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginInput { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var result = await signIn.PasswordSignInAsync(input.Email, input.Password, input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction("Index", "Appointments");
        ModelState.AddModelError(string.Empty, "Invalid sign-in attempt.");
        return View(input);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction("Index", "Home"); }
}

public sealed class RegisterInput
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), MinLength(10)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}

public sealed class LoginInput
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
