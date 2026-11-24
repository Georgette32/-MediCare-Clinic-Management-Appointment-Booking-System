using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Filters;

public static class MustChangePasswordPolicy
{
    public static bool ShouldRedirect(bool mustChangePassword, PathString path) =>
        mustChangePassword && !IsStaticAsset(path) &&
        !path.Equals("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase) &&
        !path.Equals("/Account/Logout", StringComparison.OrdinalIgnoreCase);

    public static bool IsStaticAsset(PathString path) =>
        path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
        path.Value?.EndsWith(".styles.css", StringComparison.OrdinalIgnoreCase) == true;
}
