namespace MediCare.Web.Models;

/// <summary>
/// The viewer's colour theme: Light, Dark, or Auto (follow the device). Stored in a cookie and rendered by the
/// server as &lt;html data-theme&gt;, so the page arrives in the right theme without any script (the CSP allows none inline).
/// </summary>
public static class ThemePreference
{
    public const string CookieName = "mc-theme";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Auto = "auto";

    /// <summary>Returns a known theme for any input; anything unrecognised falls back to Auto.</summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => Auto
    };

    public static string Label(string theme) => theme switch
    {
        Light => "Light",
        Dark => "Dark",
        _ => "Auto"
    };
}
