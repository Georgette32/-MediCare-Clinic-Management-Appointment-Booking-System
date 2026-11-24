namespace MediCare.Application.Analytics;

/// <summary>
/// Event sweep that integrates the number of patients in a state over the open time. For each interval [Start, End) a
/// +1 event is placed at Start and a -1 event at End. Between consecutive events the count is constant, and the count
/// times the overlap of that segment with the open domain is accumulated. Dividing the integral by the open time gives
/// the time-average count.
/// </summary>
public static class TimeAverageSweep
{
    /// <summary>Integral of the count over the domain, in hours (patient-hours).</summary>
    public static double IntegrateHours(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> items,
        IReadOnlyList<OpenInterval> domain)
    {
        var events = new List<(DateTimeOffset Time, int Delta)>();
        foreach (var (start, end) in items)
        {
            if (end <= start)
                continue;
            events.Add((start, 1));
            events.Add((end, -1));
        }
        if (events.Count == 0)
            return 0;

        events.Sort((a, b) => a.Time.CompareTo(b.Time));

        double totalSeconds = 0;
        var count = 0;
        var previous = events[0].Time;
        foreach (var (time, delta) in events)
        {
            if (time > previous)
            {
                totalSeconds += count * OverlapSeconds(previous, time, domain);
                previous = time;
            }
            count += delta;
        }

        return totalSeconds / 3600.0;
    }

    private static double OverlapSeconds(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<OpenInterval> domain)
    {
        double seconds = 0;
        foreach (var interval in domain)
        {
            var start = from > interval.Start ? from : interval.Start;
            var end = to < interval.End ? to : interval.End;
            if (end > start)
                seconds += (end - start).TotalSeconds;
        }
        return seconds;
    }
}
