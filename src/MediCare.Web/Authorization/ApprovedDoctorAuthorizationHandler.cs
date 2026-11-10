using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace MediCare.Web.Authorization;

public sealed class ApprovedDoctorRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Re-checks doctor approval against the database on every authorization decision.
/// The role alone is never enough: a pending, rejected or suspended doctor must not
/// reach the clinical dashboard.
/// </summary>
public sealed class ApprovedDoctorAuthorizationHandler : AuthorizationHandler<ApprovedDoctorRequirement>
{
    private readonly IDoctorRepository _doctors;
    private readonly IIdentityService _identity;

    public ApprovedDoctorAuthorizationHandler(IDoctorRepository doctors, IIdentityService identity)
    {
        _doctors = doctors;
        _identity = identity;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApprovedDoctorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (!context.User.IsInRole(Roles.Doctor))
        {
            return;
        }

        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        if (!await _identity.IsActiveAsync(userId))
        {
            return;
        }

        var profile = await _doctors.GetByUserIdAsync(userId, false);
        if (profile is not null && profile.IsApproved)
        {
            context.Succeed(requirement);
        }
    }
}
