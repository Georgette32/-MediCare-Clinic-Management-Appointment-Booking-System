using System.Globalization;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

public enum CalendarDayState { Blank, Past, Off, Holiday, Open }

public sealed record CalendarDay(DateOnly? Date, CalendarDayState State, bool IsToday, bool IsSelected, string? Note);

/// <summary>
/// One month of a doctor's booking calendar. A day is Open when the doctor has weekly hours on that weekday and is not
/// on holiday; slot counts are not computed here, so the month costs no extra database work. Weeks start on Saturday.
/// </summary>
public sealed class DoctorCalendarMonth
{
    public const int MonthsAhead = 6;

    public DateOnly Month { get; init; }
    public IReadOnlyList<CalendarDay> Days { get; init; } = [];
    public DateOnly? PreviousMonth { get; init; }
    public DateOnly? NextMonth { get; init; }
    public string Title => Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static readonly string[] WeekdayLabels = ["Sat", "Sun", "Mon", "Tue", "Wed", "Thu", "Fri"];

    public static bool IsOpen(DateOnly date, DateOnly today, IReadOnlyList<WeeklyHourItem> weekly, IReadOnlyList<HolidayPeriod> holidays) =>
        date >= today && weekly.Any(hour => hour.Day == date.DayOfWeek) && HolidayOn(date, holidays) is null;

    /// <summary>The first open day from <paramref name="from"/> within the bookable range, or null.</summary>
    public static DateOnly? FirstOpenDay(DateOnly from, DateOnly today, IReadOnlyList<WeeklyHourItem> weekly, IReadOnlyList<HolidayPeriod> holidays)
    {
        var last = LastBookableDay(today);
        for (var day = from < today ? today : from; day <= last; day = day.AddDays(1))
            if (IsOpen(day, today, weekly, holidays))
                return day;
        return null;
    }

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly LastBookableDay(DateOnly today) => FirstOfMonth(today).AddMonths(MonthsAhead + 1).AddDays(-1);

    public static DoctorCalendarMonth Build(
        DateOnly month, DateOnly today, DateOnly selected, IReadOnlyList<WeeklyHourItem> weekly, IReadOnlyList<HolidayPeriod> holidays)
    {
        var first = FirstOfMonth(month);
        var earliest = FirstOfMonth(today);
        var latest = earliest.AddMonths(MonthsAhead);
        if (first < earliest) first = earliest;
        if (first > latest) first = latest;

        var days = new List<CalendarDay>();
        var leading = ((int)first.DayOfWeek + 1) % 7;   // Saturday = 0
        for (var index = 0; index < leading; index++)
            days.Add(new CalendarDay(null, CalendarDayState.Blank, false, false, null));

        for (var date = first; date.Month == first.Month; date = date.AddDays(1))
        {
            var holiday = HolidayOn(date, holidays);
            var state = date < today ? CalendarDayState.Past
                : holiday is not null ? CalendarDayState.Holiday
                : weekly.Any(hour => hour.Day == date.DayOfWeek) ? CalendarDayState.Open
                : CalendarDayState.Off;
            var note = holiday is null ? null : string.IsNullOrWhiteSpace(holiday.Reason) ? "Holiday" : "Holiday: " + holiday.Reason;
            days.Add(new CalendarDay(date, state, date == today, date == selected, note));
        }

        while (days.Count % 7 != 0)
            days.Add(new CalendarDay(null, CalendarDayState.Blank, false, false, null));

        return new DoctorCalendarMonth
        {
            Month = first,
            Days = days,
            PreviousMonth = first > earliest ? first.AddMonths(-1) : null,
            NextMonth = first < latest ? first.AddMonths(1) : null
        };
    }

    private static HolidayPeriod? HolidayOn(DateOnly date, IReadOnlyList<HolidayPeriod> holidays) =>
        holidays.FirstOrDefault(holiday => holiday.StartDate <= date && date <= holiday.EndDate);
}

/// <summary>The doctor page: profile, the month calendar and the chosen day's free times.</summary>
public sealed class DoctorDetailsPageModel
{
    public required DoctorProfileView Profile { get; init; }
    public required DoctorCalendarMonth Calendar { get; init; }
    public required DateOnly Today { get; init; }

    /// <summary>False when the selected date is not a working day (past, off or holiday), so no times are shown.</summary>
    public bool SelectedIsOpen { get; init; }
}
