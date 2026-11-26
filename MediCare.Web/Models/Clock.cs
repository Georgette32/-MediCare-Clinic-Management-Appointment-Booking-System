using System.Globalization;

namespace MediCare.Web.Models;

/// <summary>
/// How clock times are shown to people: 12-hour with AM/PM ("2:30 PM"), always in English whatever the server's culture.
/// Values that forms and links post back keep the 24-hour "HH:mm" form the server parses.
/// </summary>
public static class Clock
{
    public const string Format = "h:mm tt";
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
}
