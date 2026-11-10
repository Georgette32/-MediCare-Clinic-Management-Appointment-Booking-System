using System.Security.Claims;
using MediCare.Application.Common.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Identity;

/// <summary>
/// Adds display/status claims to the authentication cookie. Authorization decisions
/// that must be immediate (doctor approval, account activation) are still enforced
/// server-side against the database, never by these claims alone.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(CustomClaimTypes.FullName, user.FullName));
        identity.AddClaim(new Claim(CustomClaimTypes.IsActive, user.IsActive ? "true" : "false"));
        return identity;
    }
}
