using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Filters;
using Microsoft.AspNetCore.Identity;

namespace MediCare.Web.Middleware;

/// <summary>
/// Per-request session guard for signed-in users. A deactivated or deleted account is signed out and sent to the login
/// page. A user who must change the password is redirected to the change-password page.
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        if (context.User.Identity?.IsAuthenticated == true && !MustChangePasswordPolicy.IsStaticAsset(context.Request.Path))
        {
            var user = await users.GetUserAsync(context.User);
            if (!AccountAccessPolicy.IsSessionAllowed(user is not null, user?.IsActive ?? false))
            {
                await signIn.SignOutAsync();
                context.Response.Redirect("/Account/Login");
                return;
            }

            if (MustChangePasswordPolicy.ShouldRedirect(user!.MustChangePassword, context.Request.Path))
            {
                context.Response.Redirect("/Account/ChangePassword");
                return;
            }
        }

        await next(context);
    }
}
