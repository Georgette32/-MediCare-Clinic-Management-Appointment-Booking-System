using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Identity;

/// <summary>Claim types MediCare adds to the sign-in cookie.</summary>
public static class MediCareClaims
{
    /// <summary>The account's full name, for display only (the top bar). Never used for authorization.</summary>
    public const string FullName = "medicare:full_name";
}

/// <summary>Adds the account's full name to the signed-in identity so pages can greet people by name.</summary>
public sealed class MediCareClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.FullName))
            identity.AddClaim(new Claim(MediCareClaims.FullName, user.FullName.Trim()));
        return identity;
    }
}
