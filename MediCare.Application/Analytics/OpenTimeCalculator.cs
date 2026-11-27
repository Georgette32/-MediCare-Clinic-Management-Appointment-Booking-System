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
        DateTimeOffset? cutoffUtc,
        IReadOnlyList<AnalyticsWorkingHourSnapshotRow>? workingHourSnapshots = null)
    {
        if (toDate < fromDate)
            return [];

        // Windows with recorded history are read from the snapshot in force on each day; windows without any snapshot
        // (legacy) keep using their current row, which is the only state they have ever had.
        var snapshotsByWindow = (workingHourSnapshots ?? []).GroupBy(item => item.WorkingHourId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            if (holidays.Any(holiday => holiday.StartDate <= day && holiday.EndDate >= day))
                continue;

            var dayBreaks = breaks.Where(item => item.Date == day)
                .Select(item => (Start: toUtc(day, item.StartTime), End: toUtc(day, item.EndTime)))
                .ToArray();

            var dayWindows = workingHours
                .Where(item => item.DayOfWeek == day.DayOfWeek && !(item.WorkingHourId is { } id && snapshotsByWindow.ContainsKey(id)))
                .Select(item => (StartTime: item.StartTime, EndTime: item.EndTime))
                .Concat(snapshotsByWindow.Values
                    .Select(rows => StateOn(rows, day))
                    .OfType<AnalyticsWorkingHourSnapshotRow>()
                    .Where(row => row.IsActive && row.DayOfWeek == day.DayOfWeek)
                    .Select(row => (StartTime: row.StartTime, EndTime: row.EndTime)))
                .OrderBy(item => item.StartTime);

            foreach (var hour in dayWindows)
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

    /// <summary>
    /// Adds the time the doctor demonstrably worked outside the scheduled open time: every work interval itself (a
    /// consultation in a break, for example), and on each clinic-local day the stretch from the first work start to the
    /// scheduled start and from the scheduled end to the last work end. A day with work and no scheduled time adds the
    /// span from its first start to its last end. Intervals are clipped to the cutoff (live) and merged.
    /// </summary>
    public static IReadOnlyList<OpenInterval> IncludeWork(
        IReadOnlyList<OpenInterval> scheduled,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> work,
        TimeZoneInfo clinicZone,
        DateTimeOffset? cutoffUtc)
    {
        var workItems = work.Where(item => item.End > item.Start).ToArray();
        if (workItems.Length == 0)
            return scheduled;

        var items = scheduled.Select(interval => (interval.Start, interval.End)).ToList();
        items.AddRange(workItems);

        var envelopes = scheduled
            .GroupBy(interval => LocalDate(interval.Start, clinicZone))
            .ToDictionary(group => group.Key, group => (Start: group.Min(interval => interval.Start), End: group.Max(interval => interval.End)));
        foreach (var day in workItems.GroupBy(item => LocalDate(item.Start, clinicZone)))
        {
            var first = day.Min(item => item.Start);
            var last = day.Max(item => item.End);
            if (envelopes.TryGetValue(day.Key, out var envelope))
            {
                if (first < envelope.Start)
                    items.Add((first, envelope.Start));
                if (last > envelope.End)
                    items.Add((envelope.End, last));
            }
            else
            {
                items.Add((first, last));
            }
        }

        var clipped = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var (start, end) in items)
        {
            var effectiveEnd = cutoffUtc is { } cutoff && cutoff < end ? cutoff : end;
            if (effectiveEnd > start)
                clipped.Add((start, effectiveEnd));
        }

        return Merge(clipped).Select(item => new OpenInterval(item.Start, item.End)).ToArray();
    }

    /// <summary>The state in force on <paramref name="day"/>: the latest EffectiveFrom on or before it; the latest recording wins a tie.</summary>
    private static AnalyticsWorkingHourSnapshotRow? StateOn(IEnumerable<AnalyticsWorkingHourSnapshotRow> rows, DateOnly day) =>
        rows.Where(row => row.EffectiveFrom <= day)
            .OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.RecordedAtUtc)
            .FirstOrDefault();

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo clinicZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, clinicZone).DateTime);

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
