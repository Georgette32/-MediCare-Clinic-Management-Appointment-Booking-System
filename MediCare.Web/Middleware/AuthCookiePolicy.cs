using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Middleware;

/// <summary>Auth cookie flags. Secure is always sent outside Development so plain HTTP is never used for the cookie.</summary>
public static class AuthCookiePolicy
{
    public static void Apply(CookieBuilder cookie, bool isDevelopment)
    {
        cookie.HttpOnly = true;
        cookie.SameSite = SameSiteMode.Lax;
        cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    }
}
