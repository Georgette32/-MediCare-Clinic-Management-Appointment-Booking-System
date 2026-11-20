using System.Security.Claims;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using Microsoft.AspNetCore.Identity;
using MediCare.Infrastructure.Identity;

namespace MediCare.Web.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? _httpContextAccessor.HttpContext?.User.Identity?.Name;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => _httpContextAccessor.HttpContext?.User.IsInRole(role) ?? false;
}

public sealed class AppUrlProvider : IAppUrlProvider
{
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AppUrlProvider(LinkGenerator linkGenerator, IHttpContextAccessor httpContextAccessor)
    {
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

    public string EmailConfirmationUrl(Guid userId, string token) =>
        BuildAbsoluteUrl("ConfirmEmail", new { userId, token });

    public string PasswordResetUrl(Guid userId, string token) =>
        BuildAbsoluteUrl("ResetPassword", new { userId, token });

    public string LoginUrl() => BuildAbsoluteUrl("Login", null);

    private string BuildAbsoluteUrl(string action, object? values)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        // Use the ambient-free overload so account links created while inside an area
        // (for example /Admin) never become /Admin/Account/...
        var path = _linkGenerator.GetPathByAction(action, "Account", values) ?? $"/Account/{action}";
        var pathBase = httpContext?.Request.PathBase.ToString() ?? string.Empty;

        if (httpContext is not null)
        {
            return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{pathBase}{path}";
        }

        return $"http://localhost{pathBase}{path}";
    }
}
