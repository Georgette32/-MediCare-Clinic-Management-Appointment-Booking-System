using MediCare.Infrastructure.Time;
using MediCare.Web.Models;

namespace MediCare.Tests;

public sealed class ClinicTimeTests
{
    private static readonly TimeZoneInfo Cairo = ClinicTimeZoneResolver.Resolve("Africa/Cairo");

    [Fact]
    public void Formats_utc_instants_in_clinic_local_time()
    {
        // 2026-11-24 is standard time in Cairo (UTC+2).
        var utc = new DateTimeOffset(2026, 11, 24, 11, 19, 0, TimeSpan.Zero);
        Assert.Equal("2026-11-24 13:19", ClinicTime.Format(utc, Cairo));
        Assert.Equal("13:19", ClinicTime.Format(utc, Cairo, "HH:mm"));
    }

    [Fact]
    public void Crosses_the_local_date_boundary_and_handles_null()
    {
        var utc = new DateTimeOffset(2026, 11, 24, 22, 30, 0, TimeSpan.Zero);
        Assert.Equal("2026-11-25 00:30", ClinicTime.Format(utc, Cairo));
        Assert.Equal("-", ClinicTime.Format((DateTimeOffset?)null, Cairo));
    }
}
