using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Filters;

/// <summary>
/// A patient who signed up but has not entered the email code may only use the verify page and its actions (and sign
/// out). Accounts created before email verification existed have <c>requiresVerification = false</c> and are never
/// redirected.
/// </summary>
public static class EmailVerificationPolicy
{
    public const string VerifyPath = "/Account/VerifyEmail";

    private static readonly string[] AllowedPaths =
    [
        VerifyPath,
        "/Account/ResendCode",
        "/Account/ChangeEmail",
        "/Account/Logout",
        "/Theme/Set"
    ];

    public static bool ShouldRedirect(bool requiresVerification, bool emailConfirmed, PathString path) =>
        requiresVerification && !emailConfirmed &&
        !MustChangePasswordPolicy.IsStaticAsset(path) &&
        !path.StartsWithSegments("/img", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/fonts", StringComparison.OrdinalIgnoreCase) &&
        !AllowedPaths.Any(allowed => path.Equals(allowed, StringComparison.OrdinalIgnoreCase));
}
