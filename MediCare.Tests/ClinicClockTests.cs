using MediCare.Infrastructure.Time;

namespace MediCare.Tests;

public sealed class ClinicClockTests
{
    [Fact]
    public void Clinic_day_boundary_uses_local_date_without_constructing_midnight_instant()
    {
        var zone = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 21, 21, 59, 59, TimeSpan.Zero));
        var clinicClock = new SystemClinicClock(clock, zone);

        Assert.Equal(new DateOnly(2026, 11, 21), clinicClock.Today);
        clock.UtcNow = new DateTimeOffset(2026, 11, 21, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 22), clinicClock.Today);
    }

    [Fact]
    public void Clinic_clock_uses_real_cairo_dst_transition_rules()
    {
        var zone = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
        var transition = FindOffsetTransition(zone, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(35));
        var before = transition.AddMinutes(-1);
        var after = transition;

        Assert.NotEqual(zone.GetUtcOffset(before), zone.GetUtcOffset(after));
        var beforeClock = new SystemClinicClock(new FixedTimeProvider(before), zone);
        var afterClock = new SystemClinicClock(new FixedTimeProvider(after), zone);
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(before, zone).DateTime), beforeClock.Today);
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime), afterClock.Today);

        Assert.Equal(new DateOnly(2026, 10, 29), beforeClock.Today);
        Assert.Equal(new DateOnly(2026, 10, 29), afterClock.Today);
    }

    [Fact]
    public void Clinic_time_zone_resolver_accepts_windows_and_iana_ids()
    {
        var iana = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
        Assert.Equal("Africa/Cairo", iana.Id);

        Assert.True(TimeZoneInfo.TryConvertIanaIdToWindowsId("Africa/Cairo", out var windowsId));
        Assert.NotNull(windowsId);
        var windows = ClinicTimeZoneResolver.Resolve(windowsId!);
        Assert.Equal(iana.GetUtcOffset(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)),
            windows.GetUtcOffset(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));
    }

    private static DateTimeOffset FindOffsetTransition(TimeZoneInfo zone, DateTimeOffset start, TimeSpan searchWindow)
    {
        var previous = zone.GetUtcOffset(start);
        for (var elapsed = TimeSpan.FromMinutes(1); elapsed <= searchWindow; elapsed += TimeSpan.FromMinutes(1))
        {
            var candidate = start + elapsed;
            var offset = zone.GetUtcOffset(candidate);
            if (offset != previous)
                return candidate;
            previous = offset;
        }

        throw new Xunit.Sdk.XunitException("No DST offset change was found in the configured Cairo timezone data.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
