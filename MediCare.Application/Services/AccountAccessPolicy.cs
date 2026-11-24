namespace MediCare.Application.Services;

/// <summary>Decides whether an account may hold a session. Shared by sign-in and the per-request session guard.</summary>
public static class AccountAccessPolicy
{
    /// <summary>A missing or deactivated account can never sign in or keep a session.</summary>
    public static bool IsSessionAllowed(bool accountFound, bool isActive) => accountFound && isActive;
}
