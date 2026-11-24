using MediCare.Application.Abstractions;

namespace MediCare.Application.Analytics;

/// <summary>A half-open UTC interval [Start, End) during which the doctor was open.</summary>
public readonly record struct OpenInterval(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// Builds the observed open time T for one doctor: for each clinic-local date in the period, the active working windows
/// (with an Approved extension replacing the window end it was recorded against, mirroring DoctorAvailabilityService),
/// minus dated breaks, skipping holiday dates. When a cutoff is given (live), intervals are clipped to it.
/// </summary>
public static class OpenTimeCalculator
{
    public static IReadOnlyList<OpenInterval> Build(
        DateOnly fromDate,
        DateOnly toDate,
        Func<DateOnly, TimeOnly, DateTimeOffset> toUtc,
        IReadOnlyList<AnalyticsWorkingHourRow> workingHours,
        IReadOnlyList<AnalyticsHolidayRow> holidays,
        IReadOnlyList<AnalyticsExtensionRow> extensions,
        IReadOnlyList<AnalyticsBreakRow> breaks,
        DateTimeOffset? cutoffUtc)
    {
        if (toDate < fromDate)
            return [];

        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            if (holidays.Any(holiday => holiday.StartDate <= day && holiday.EndDate >= day))
                continue;

            var dayBreaks = breaks.Where(item => item.Date == day)
                .Select(item => (Start: toUtc(day, item.StartTime), End: toUtc(day, item.EndTime)))
                .ToArray();

            foreach (var hour in workingHours.Where(item => item.DayOfWeek == day.DayOfWeek).OrderBy(item => item.StartTime))
            {
                var extension = extensions.FirstOrDefault(item => item.Date == day && item.OriginalEndTime == hour.EndTime);
                var endTime = extension?.ExtendedEndTime ?? hour.EndTime;
                var start = toUtc(day, hour.StartTime);
                var end = toUtc(day, endTime);
                if (end <= start)
                    continue;

                intervals.AddRange(Subtract((start, end), dayBreaks));
            }
        }

        var clipped = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var (start, end) in intervals)
        {
            var effectiveEnd = cutoffUtc is { } cutoff && cutoff < end ? cutoff : end;
            if (effectiveEnd > start)
                clipped.Add((start, effectiveEnd));
        }

        return Merge(clipped).Select(item => new OpenInterval(item.Start, item.End)).ToArray();
    }

    /// <summary>Total open time of the intervals, in hours.</summary>
    public static double TotalHours(IEnumerable<OpenInterval> intervals) =>
        intervals.Sum(item => item.Length.TotalHours);

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Subtract(
        (DateTimeOffset Start, DateTimeOffset End) window,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> cuts)
    {
        var pieces = new List<(DateTimeOffset Start, DateTimeOffset End)> { window };
        foreach (var cut in cuts)
        {
            var next = new List<(DateTimeOffset Start, DateTimeOffset End)>();
            foreach (var piece in pieces)
            {
                if (cut.End <= piece.Start || cut.Start >= piece.End)
                {
                    next.Add(piece);
                    continue;
                }
                if (cut.Start > piece.Start)
                    next.Add((piece.Start, cut.Start));
                if (cut.End < piece.End)
                    next.Add((cut.End, piece.End));
            }
            pieces = next;
        }
        return pieces;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Merge(List<(DateTimeOffset Start, DateTimeOffset End)> items)
    {
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var item in items.OrderBy(entry => entry.Start))
        {
            if (merged.Count > 0 && item.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, item.End > last.End ? item.End : last.End);
            }
            else
            {
                merged.Add(item);
            }
        }
        return merged;
    }
}
