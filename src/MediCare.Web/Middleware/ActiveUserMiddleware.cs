using System.Security.Claims;
using MediCare.Application.Interfaces.Identity;
using Microsoft.AspNetCore.Identity;
using MediCare.Infrastructure.Identity;

namespace MediCare.Web.Middleware;

/// <summary>
/// Immediately blocks deactivated accounts even when they still hold a valid cookie.
/// </summary>
public sealed class ActiveUserMiddleware
{
    private readonly RequestDelegate _next;

    public ActiveUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IIdentityService identity, SignInManager<ApplicationUser> signInManager)
    {
        if (context.User.Identity?.IsAuthenticated == true && !IsStaticAsset(context.Request.Path))
        {
            var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdValue, out var userId))
            {
                var isActive = await identity.IsActiveAsync(userId, context.RequestAborted);
                if (!isActive)
                {
                    await signInManager.SignOutAsync();
                    context.Response.Redirect("/Account/Login?reason=inactive");
                    return;
                }
            }
        }

        await _next(context);
    }

    private static bool IsStaticAsset(PathString path) =>
        path.StartsWithSegments("/css") ||
        path.StartsWithSegments("/js") ||
        path.StartsWithSegments("/lib") ||
        path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/favicon.ico");
}
