using MediCare.Application.Services;
using MediCare.Web.Models;

namespace MediCare.Tests;

public sealed class DoctorCalendarTests
{
    // Thursday 26 November 2026.
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static readonly WeeklyHourItem[] SundayAndTuesday =
    [
        new(DayOfWeek.Sunday, new TimeOnly(9, 0), new TimeOnly(13, 0)),
        new(DayOfWeek.Tuesday, new TimeOnly(16, 0), new TimeOnly(20, 0))
    ];

    [Fact]
    public void Only_working_days_from_today_are_open_and_holidays_are_not()
    {
        HolidayPeriod[] holidays = [new(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1), "Conference")];
        var month = DoctorCalendarMonth.Build(new DateOnly(2026, 12, 1), Today, new DateOnly(2026, 12, 6), SundayAndTuesday, holidays);

        var open = month.Days.Where(day => day.State == CalendarDayState.Open).Select(day => day.Date!.Value).ToArray();
        Assert.All(open, date => Assert.Contains(date.DayOfWeek, new[] { DayOfWeek.Sunday, DayOfWeek.Tuesday }));
        Assert.DoesNotContain(new DateOnly(2026, 12, 1), open);
        Assert.Equal(CalendarDayState.Holiday, month.Days.Single(day => day.Date == new DateOnly(2026, 12, 1)).State);
        Assert.Equal("Holiday: Conference", month.Days.Single(day => day.Date == new DateOnly(2026, 12, 1)).Note);
        Assert.Equal(CalendarDayState.Off, month.Days.Single(day => day.Date == new DateOnly(2026, 12, 2)).State);
        Assert.True(month.Days.Single(day => day.Date == new DateOnly(2026, 12, 6)).IsSelected);
    }

    [Fact]
    public void Weeks_start_on_Saturday_and_fill_whole_rows()
    {
        // 1 December 2026 is a Tuesday: Sat, Sun, Mon are blank before it.
        var month = DoctorCalendarMonth.Build(new DateOnly(2026, 12, 1), Today, Today, SundayAndTuesday, []);

        Assert.Equal(3, month.Days.TakeWhile(day => day.Date is null).Count());
        Assert.Equal(0, month.Days.Count % 7);
        Assert.Equal(31, month.Days.Count(day => day.Date is not null));
    }

    [Fact]
    public void Past_days_this_month_are_closed_and_today_is_marked()
    {
        var month = DoctorCalendarMonth.Build(Today, Today, Today, SundayAndTuesday, []);

        Assert.Equal(CalendarDayState.Past, month.Days.Single(day => day.Date == new DateOnly(2026, 11, 22)).State);   // a past Sunday
        Assert.True(month.Days.Single(day => day.Date == Today).IsToday);
        Assert.Null(month.PreviousMonth);
        Assert.Equal(new DateOnly(2026, 12, 1), month.NextMonth);
    }

    [Fact]
    public void Months_stay_between_this_month_and_six_ahead()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), DoctorCalendarMonth.Build(new DateOnly(2025, 1, 1), Today, Today, SundayAndTuesday, []).Month);
        var last = DoctorCalendarMonth.Build(new DateOnly(2030, 1, 1), Today, Today, SundayAndTuesday, []);
        Assert.Equal(new DateOnly(2027, 5, 1), last.Month);
        Assert.Null(last.NextMonth);
    }

    [Fact]
    public void First_open_day_skips_days_off_and_holidays()
    {
        HolidayPeriod[] holidays = [new(new DateOnly(2026, 11, 29), new DateOnly(2026, 11, 29), null)];
        // Thursday today; Sunday 29th is a holiday, so Tuesday 1 December is next.
        Assert.Equal(new DateOnly(2026, 12, 1), DoctorCalendarMonth.FirstOpenDay(Today, Today, SundayAndTuesday, holidays));
        Assert.Null(DoctorCalendarMonth.FirstOpenDay(Today, Today, [], []));
    }
}
