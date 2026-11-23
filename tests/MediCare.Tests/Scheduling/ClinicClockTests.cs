using MediCare.Infrastructure.Time;

namespace MediCare.Tests.Scheduling;

public sealed class ClinicClockTests
{
    [Fact]
    public void Clinic_day_boundary_uses_local_date_without_constructing_midnight_instant()
    {
        var clock = new FixedTimeProvider(new DateTime(2026, 11, 21, 23, 59, 59, DateTimeKind.Utc));
        var clinicClock = new SystemClinicClock(clock);

        Assert.Equal(new DateOnly(2026, 11, 21), clinicClock.Today);
        clock.UtcNow = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 11, 22), clinicClock.Today);
    }

    [Fact]
    public void Clinic_clock_current_time_uses_the_configured_clinic_timezone()
    {
        var now = new DateTime(2026, 11, 23, 10, 15, 0, DateTimeKind.Utc);
        var clinicClock = new SystemClinicClock(new FixedTimeProvider(now));

        Assert.Equal(TimeOnly.FromDateTime(now), clinicClock.CurrentTime);
    }

    [Fact]
    public void Clinic_clock_uses_real_cairo_dst_transition_rules()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var rule = FindUpcomingDaylightRule(zone);
        var year = Math.Max(DateTime.UtcNow.Year, rule.DateStart.Year);
        var springTransition = GetTransitionDateTime(year, rule.DaylightTransitionStart);
        var invalidLocal = springTransition.Add(rule.DaylightDelta / 2);
        var firstValidLocal = springTransition.Add(rule.DaylightDelta);
        var fallTransition = GetTransitionDateTime(year, rule.DaylightTransitionEnd);
        var ambiguousLocal = fallTransition.Subtract(rule.DaylightDelta);
        var clock = new SystemClinicClock(new FixedTimeProvider(DateTime.UtcNow));

        Assert.True(zone.IsInvalidTime(invalidLocal));
        Assert.False(zone.IsInvalidTime(firstValidLocal));
        Assert.Equal(
            DateTime.SpecifyKind(invalidLocal, DateTimeKind.Utc),
            clock.ToUtc(DateOnly.FromDateTime(invalidLocal), TimeOnly.FromDateTime(invalidLocal)));

        Assert.True(zone.IsAmbiguousTime(ambiguousLocal));
        Assert.Equal(
            DateTime.SpecifyKind(ambiguousLocal, DateTimeKind.Utc),
            clock.ToUtc(DateOnly.FromDateTime(ambiguousLocal), TimeOnly.FromDateTime(ambiguousLocal)));
    }

    [Fact]
    public void Clinic_time_zone_resolver_accepts_windows_and_iana_ids()
    {
        var iana = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        Assert.Equal("Africa/Cairo", iana.Id);

        Assert.True(TimeZoneInfo.TryConvertIanaIdToWindowsId("Africa/Cairo", out var windowsId));
        Assert.NotNull(windowsId);
        var windows = TimeZoneInfo.FindSystemTimeZoneById(windowsId!);
        var instant = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(iana.GetUtcOffset(instant), windows.GetUtcOffset(instant));
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

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
    }
}
