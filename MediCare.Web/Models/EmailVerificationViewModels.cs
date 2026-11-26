namespace MediCare.Web.Models;

/// <summary>Everything the verify page needs to draw its first state; the script keeps it current from the JSON actions.</summary>
public sealed class VerifyEmailViewModel
{
    public string FirstName { get; init; } = string.Empty;
    public string MaskedEmail { get; init; } = string.Empty;

    /// <summary>Unix milliseconds when the current code stops working; null when there is no usable code.</summary>
    public long? ExpiresAtUnixMs { get; init; }

    /// <summary>Server time when the page was rendered, so the countdown does not depend on the device clock.</summary>
    public long ServerNowUnixMs { get; init; }

    public int LifetimeSeconds { get; init; }
    public int CooldownSeconds { get; init; }
    public int TriesLeft { get; init; }
    public bool SendFailed { get; init; }
}
