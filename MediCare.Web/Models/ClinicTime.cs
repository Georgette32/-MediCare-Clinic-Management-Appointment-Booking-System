namespace MediCare.Web.Models;

/// <summary>Formats stored UTC instants in the clinic's configured time zone for display.</summary>
public static class ClinicTime
{
    public static string Format(DateTimeOffset utc, TimeZoneInfo clinicZone, string format = "yyyy-MM-dd h:mm tt") =>
        TimeZoneInfo.ConvertTime(utc, clinicZone).ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    public static string Format(DateTimeOffset? utc, TimeZoneInfo clinicZone, string format = "yyyy-MM-dd h:mm tt") =>
        utc is null ? "-" : Format(utc.Value, clinicZone, format);
}
