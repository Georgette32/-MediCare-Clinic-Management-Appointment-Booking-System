using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>Saves the Light / Dark / Auto choice and returns the viewer to the page they were on.</summary>
public class ThemeController : Controller
{
    [HttpPost]
    public IActionResult Set(string? theme, string? returnUrl)
    {
        var value = ThemePreference.Normalize(theme);
        Response.Cookies.Append(ThemePreference.CookieName, value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });

        // site.js saves the choice in the background after switching the page in place; no page to return to.
        if (string.Equals(Request.Headers["X-Requested-With"], "fetch", StringComparison.Ordinal))
            return NoContent();

        // Only ever return to a page on this site.
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
    }
}
