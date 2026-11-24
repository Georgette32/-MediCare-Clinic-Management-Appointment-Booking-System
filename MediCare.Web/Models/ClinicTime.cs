namespace MediCare.Web.Models;

/// <summary>Formats stored UTC instants in the clinic's configured time zone for display.</summary>
public static class ClinicTime
{
    public static string Format(DateTimeOffset utc, TimeZoneInfo clinicZone, string format = "yyyy-MM-dd HH:mm") =>
        TimeZoneInfo.ConvertTime(utc, clinicZone).ToString(format);

    public static string Format(DateTimeOffset? utc, TimeZoneInfo clinicZone, string format = "yyyy-MM-dd HH:mm") =>
        utc is null ? "-" : Format(utc.Value, clinicZone, format);
}
