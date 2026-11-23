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
    public void Clinic_clock_current_time_uses_the_configured_clinic_timezone()
    {
        var zone = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
        var now = new DateTimeOffset(2026, 11, 23, 10, 15, 0, TimeSpan.Zero);
        var clinicClock = new SystemClinicClock(new FixedTimeProvider(now), zone);

        var expected = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        Assert.Equal(expected, clinicClock.CurrentTime);
    }

    [Fact]
    public void Clinic_clock_uses_real_cairo_dst_transition_rules()
    {
        var zone = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
        var rule = FindUpcomingDaylightRule(zone);
        var year = Math.Max(DateTime.UtcNow.Year, rule.DateStart.Year);
        var springTransition = GetTransitionDateTime(year, rule.DaylightTransitionStart);
        var invalidLocal = springTransition.Add(rule.DaylightDelta / 2);
        var firstValidLocal = springTransition.Add(rule.DaylightDelta);
        var fallTransition = GetTransitionDateTime(year, rule.DaylightTransitionEnd);
        var ambiguousLocal = fallTransition.Subtract(rule.DaylightDelta);
        var clock = new SystemClinicClock(new FixedTimeProvider(DateTimeOffset.UtcNow), zone);

        Assert.True(zone.IsInvalidTime(invalidLocal));
        Assert.False(zone.IsInvalidTime(firstValidLocal));
        Assert.Equal(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(firstValidLocal, zone)),
            clock.ToUtc(DateOnly.FromDateTime(invalidLocal), TimeOnly.FromDateTime(invalidLocal)));

        Assert.True(zone.IsAmbiguousTime(ambiguousLocal));
        var standardOffset = zone.GetAmbiguousTimeOffsets(ambiguousLocal).Min();
        var expectedStandardUtc = new DateTimeOffset(ambiguousLocal, standardOffset).ToUniversalTime();
        Assert.Equal(expectedStandardUtc,
            clock.ToUtc(DateOnly.FromDateTime(ambiguousLocal), TimeOnly.FromDateTime(ambiguousLocal)));
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

    private static TimeZoneInfo.AdjustmentRule FindUpcomingDaylightRule(TimeZoneInfo zone)
    {
        var year = DateTime.UtcNow.Year;
        return zone.GetAdjustmentRules()
            .Where(rule => rule.DaylightDelta > TimeSpan.Zero && rule.DateEnd.Year >= year)
            .OrderByDescending(rule => Math.Max(rule.DateStart.Year, year))
            .FirstOrDefault(rule => Math.Max(rule.DateStart.Year, year) <= rule.DateEnd.Year)
            ?? throw new Xunit.Sdk.XunitException("No current or future positive daylight adjustment rule exists for Africa/Cairo.");
    }

    private static DateTime GetTransitionDateTime(int year, TimeZoneInfo.TransitionTime transition)
    {
        int day;
        if (transition.IsFixedDateRule)
        {
            day = Math.Min(transition.Day, DateTime.DaysInMonth(year, transition.Month));
        }
        else
        {
            var firstOfMonth = new DateTime(year, transition.Month, 1);
            var offset = ((int)transition.DayOfWeek - (int)firstOfMonth.DayOfWeek + 7) % 7;
            day = 1 + offset + (transition.Week - 1) * 7;
            if (transition.Week == 5 && day > DateTime.DaysInMonth(year, transition.Month))
                day -= 7;
        }

        return DateTime.SpecifyKind(new DateTime(year, transition.Month, day).Add(transition.TimeOfDay.TimeOfDay), DateTimeKind.Unspecified);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
