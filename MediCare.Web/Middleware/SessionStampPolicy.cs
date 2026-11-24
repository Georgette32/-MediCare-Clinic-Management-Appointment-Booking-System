namespace MediCare.Web.Middleware;

/// <summary>
/// A signed-in session is valid only while the security stamp in its cookie equals the account's current stamp.
/// A missing stamp on either side is treated as a match so sessions issued before stamps existed are not broken.
/// </summary>
public static class SessionStampPolicy
{
    public static bool Matches(string? cookieStamp, string? accountStamp) =>
        string.IsNullOrEmpty(cookieStamp) || string.IsNullOrEmpty(accountStamp) ||
        string.Equals(cookieStamp, accountStamp, StringComparison.Ordinal);
}
