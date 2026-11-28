using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Analytics;

/// <summary>
/// Pure queueing-analytics engine. Each doctor is an independent single server.
/// Measured values come from real timestamps. Theoretical M/G/1 values appear only when rho is below 1.
/// Exclusions: attempts tied to appointments cancelled with reason "clinic unavailable" are excluded from every
/// measure; completed attempts with invalid timestamps are excluded from service and waiting samples; service
/// durations at or above the cap are excluded from the service sample (mean, variance, mu). Busy time (utilization,
/// busy minutes) counts every served consultation, a long one only up to the cap.
/// </summary>
public static class AnalyticsCalculator
{
    /// <summary>The most a doctor's recent history counts for in the live consultation mean, in consultations.</summary>
    public const int PriorWeight = 5;

    /// <summary>The last-hour arrival rate is shown only when at least this much of the last hour was open.</summary>
    private const double MinimumRecentOpenHours = 0.25;

    public static IReadOnlyList<DoctorAnalytics> Calculate(AnalyticsSnapshot snapshot, AnalyticsRequest request)
    {
        var visitsByDoctor = snapshot.Visits.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var appointmentsByDoctor = snapshot.Appointments.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var hoursByDoctor = snapshot.WorkingHours.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var holidaysByDoctor = snapshot.Holidays.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var extensionsByDoctor = snapshot.Extensions.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var breaksByDoctor = snapshot.Breaks.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var snapshotsByDoctor = (snapshot.WorkingHourSnapshots ?? []).GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());

        return snapshot.Doctors
            .Select(doctor => ComputeDoctor(
                doctor,
                Get(visitsByDoctor, doctor.DoctorId),
                Get(appointmentsByDoctor, doctor.DoctorId),
                Get(hoursByDoctor, doctor.DoctorId),
                Get(holidaysByDoctor, doctor.DoctorId),
                Get(extensionsByDoctor, doctor.DoctorId),
                Get(breaksByDoctor, doctor.DoctorId),
                Get(snapshotsByDoctor, doctor.DoctorId),
                request))
            .ToArray();
    }

    private static DoctorAnalytics ComputeDoctor(
        AnalyticsDoctor doctor,
        IReadOnlyList<AnalyticsVisitRow> visits,
        IReadOnlyList<AnalyticsAppointmentRow> appointments,
        IReadOnlyList<AnalyticsWorkingHourRow> hours,
        IReadOnlyList<AnalyticsHolidayRow> holidays,
        IReadOnlyList<AnalyticsExtensionRow> extensions,
        IReadOnlyList<AnalyticsBreakRow> breaks,
        IReadOnlyList<AnalyticsWorkingHourSnapshotRow> workingHourSnapshots,
        AnalyticsRequest request)
    {
        var scheduledOpen = OpenTimeCalculator.Build(request.FromDate, request.ToDate, request.ToUtc, hours, holidays, extensions, breaks, request.CutoffUtc, workingHourSnapshots);
        var scheduledOpenHours = OpenTimeCalculator.TotalHours(scheduledOpen);

        // Exclusion 1: attempts tied to a "clinic unavailable" cancellation are not patients the queue served.
        var counted = visits.Where(item => !item.ClinicUnavailable).ToArray();
        var clinicUnavailable = visits.Count - counted.Length;

        var served = new List<ServedObservation>();
        var invalidTimestamps = 0;
        foreach (var visit in counted.Where(item => item.Status == QueueEntryStatus.Completed))
        {
            if (visit.ConsultationStartedAt is not { } started ||
                visit.CompletedAt is not { } completed ||
                visit.ArrivedAt > started ||
                started >= completed)
            {
                invalidTimestamps++;
                continue;
            }
            var slot = visit.AppointmentDate is { } appointmentDate && visit.AppointmentStartTime is { } appointmentStart
                ? request.ToUtc(appointmentDate, appointmentStart)
                : (DateTimeOffset?)null;
            served.Add(new ServedObservation(visit.ArrivedAt, started, completed, visit.Source, slot));
        }

        // T is the scheduled open time plus the work the doctor demonstrably did outside it (consultations after the
        // scheduled end, before the start or in a break, each counted up to the cap). Without this, overtime adds
        // arrivals and busy time but no hours, and utilization can pass 100% with nobody waiting.
        var open = OpenTimeCalculator.IncludeWork(
            scheduledOpen,
            served.Select(item => (item.Started, item.Started + CappedService(item, request.ServiceCap))),
            request.ClinicZone,
            request.CutoffUtc);
        var openHours = OpenTimeCalculator.TotalHours(open);

        // Joined the queue, then cancelled or marked No-Show before a consultation. Waiting, Called and InConsultation
        // attempts are still in the queue (live) and are not counted.
        var notSeen = counted.Count(item => item.Status is QueueEntryStatus.Cancelled or QueueEntryStatus.NoShow);
        double? notSeenRate = counted.Length > 0 ? (double)notSeen / counted.Length : null;
        var arrivalsOutsideOpen = counted.Count(item => !IsInside(item.ArrivedAt, open));

        // Exclusion 2: service durations at or above the cap. 0 < S always holds for a served observation (started < completed).
        var serviceSample = served.Where(item => item.Service < request.ServiceCap).ToArray();
        var excludedAboveCap = served.Count - serviceSample.Length;
        var serviceMinutes = serviceSample.Select(item => item.Service.TotalMinutes).ToArray();

        double? lambda = openHours > 0 ? counted.Length / openHours : null;
        double? sampleMeanMinutes = serviceMinutes.Length > 0 ? serviceMinutes.Average() : null;
        double? varianceMinutes = serviceMinutes.Length >= 2
            ? serviceMinutes.Sum(item => Math.Pow(item - sampleMeanMinutes!.Value, 2)) / (serviceMinutes.Length - 1)
            : null;

        // Live only: early in the day a few consultations make the mean jump, so it is blended with the doctor's recent
        // history, which weighs as at most PriorWeight consultations and fades as today's sample grows.
        var prior = request.ServicePriors is { } priors && priors.TryGetValue(doctor.DoctorId, out var found) && found.SampleCount > 0
            ? found
            : null;
        double? meanMinutes = sampleMeanMinutes;
        if (prior is not null)
        {
            var weight = Math.Min(PriorWeight, prior.SampleCount);
            meanMinutes = (serviceMinutes.Sum() + weight * prior.MeanMinutes) / (serviceMinutes.Length + weight);
        }
        double? stdDevMinutes = varianceMinutes is { } variance ? Math.Sqrt(variance) : null;
        double? mu = meanMinutes is { } mean ? 60.0 / mean : null;
        double? rho = lambda is { } l && mu is { } m ? l / m : null;
        var overloaded = rho is >= 1.0;

        // Busy time: a consultation at or above the cap counts as the cap, so a real long consultation is not lost.
        double? serviceHours = served.Count > 0 ? served.Sum(item => CappedService(item, request.ServiceCap).TotalHours) : null;
        double? utilization = serviceHours is { } busy && openHours > 0 ? busy / openHours : null;

        // Measured waiting and system times over served observations (valid timestamps, including long consultations).
        double? wqMinutes = served.Count > 0 ? served.Average(item => item.Wait.TotalMinutes) : null;
        double? wsMinutes = served.Count > 0 ? served.Average(item => item.Sojourn.TotalMinutes) : null;

        // Booked patients are called before walk-ins, so one average hides two different waits.
        var servedScheduled = served.Where(item => item.Source == QueueEntrySource.Scheduled).ToArray();
        var servedWalkIn = served.Where(item => item.Source == QueueEntrySource.WalkIn).ToArray();
        double? wqScheduled = servedScheduled.Length > 0 ? servedScheduled.Average(item => item.Wait.TotalMinutes) : null;
        double? wqWalkIn = servedWalkIn.Length > 0 ? servedWalkIn.Average(item => item.Wait.TotalMinutes) : null;
        var delays = servedScheduled.Where(item => item.Slot is not null).Select(DelayPastSlotMinutes).ToArray();
        double? delayPastSlot = delays.Length > 0 ? delays.Average() : null;

        // Time-average L from the event sweep over the open time.
        double? measuredLq = openHours > 0
            ? TimeAverageSweep.IntegrateHours(served.Select(item => (item.Arrived, item.Started)), open) / openHours
            : null;
        double? measuredLs = openHours > 0
            ? TimeAverageSweep.IntegrateHours(served.Select(item => (item.Arrived, item.Completed)), open) / openHours
            : null;

        // Little's Law with the served throughput n / T, because L and W are both taken over served observations. Both
        // sides come from the same timestamps, so the gap is the share of W that lies outside the open time (arrivals
        // before opening, waits during breaks, care after closing), not a measure of timestamp accuracy.
        double? servedPerHour = openHours > 0 ? served.Count / openHours : null;
        double? littleLq = servedPerHour is { } seenRate && wqMinutes is { } wqA ? seenRate * (wqA / 60.0) : null;
        double? littleLs = servedPerHour is { } seenRate2 && wsMinutes is { } wsA ? seenRate2 * (wsA / 60.0) : null;
        double? lqCheck = CheckPercent(measuredLq, littleLq);
        double? lsCheck = CheckPercent(measuredLs, littleLs);

        // Waiting room and time in clinic also count people who left (cancelled or no-show after arriving) while they were
        // there, from the recorded leave time. Live, everyone still in the queue counts up to now. Little's Law above keeps
        // served patients only, so these two averages are a separate view.
        var leavers = counted
            .Where(item => (item.Status is QueueEntryStatus.Cancelled or QueueEntryStatus.NoShow) &&
                           item.ClosedAtUtc is { } closedAt && closedAt > item.ArrivedAt)
            .ToArray();
        var leaverWaitMinutes = leavers.Select(item => (item.ClosedAtUtc!.Value - item.ArrivedAt).TotalMinutes).ToArray();
        double? meanWaitBeforeLeaving = leaverWaitMinutes.Length > 0 ? leaverWaitMinutes.Average() : null;
        var leaverStays = leavers.Select(item => (Start: item.ArrivedAt, End: item.ClosedAtUtc!.Value)).ToArray();

        var liveWaitingStays = request.CutoffUtc is { } liveCutoff
            ? counted.Where(item => item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called)
                .Select(item => (Start: item.ArrivedAt, End: liveCutoff)).ToArray()
            : Array.Empty<(DateTimeOffset Start, DateTimeOffset End)>();
        var liveClinicStays = request.CutoffUtc is { } clinicCutoff
            ? counted.Where(item => item.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
                .Select(item => (Start: item.ArrivedAt, End: clinicCutoff)).ToArray()
            : Array.Empty<(DateTimeOffset Start, DateTimeOffset End)>();

        var waitingRoomStays = served.Select(item => (Start: item.Arrived, End: item.Started)).Concat(leaverStays).Concat(liveWaitingStays);
        var clinicStays = served.Select(item => (Start: item.Arrived, End: item.Completed)).Concat(leaverStays).Concat(liveClinicStays);
        double? waitingRoomAverage = openHours > 0 ? TimeAverageSweep.IntegrateHours(waitingRoomStays, open) / openHours : null;
        double? inClinicAverage = openHours > 0 ? TimeAverageSweep.IntegrateHours(clinicStays, open) / openHours : null;

        // Arrival variability from the gaps between arrivals on the same clinic day (the overnight gap is not one).
        var gaps = counted
            .GroupBy(item => item.QueueDate)
            .SelectMany(day =>
            {
                var times = day.Select(item => item.ArrivedAt).Order().ToArray();
                return times.Zip(times.Skip(1), (earlier, later) => (later - earlier).TotalMinutes);
            })
            .ToArray();
        double? arrivalCa2 = null;
        if (gaps.Length >= 2 && gaps.Average() is var gapMean && gapMean > 0)
            arrivalCa2 = gaps.Sum(gap => Math.Pow(gap - gapMean, 2)) / (gaps.Length - 1) / (gapMean * gapMean);

        double? recentArrivalsPerHour = null;
        if (request.CutoffUtc is { } cutoff)
        {
            var windowStart = cutoff - TimeSpan.FromHours(1);
            var recentOpenHours = open.Sum(interval => Overlap(interval, windowStart, cutoff).TotalHours);
            if (recentOpenHours >= MinimumRecentOpenHours)
                recentArrivalsPerHour = counted.Count(item => item.ArrivedAt >= windowStart && item.ArrivedAt < cutoff) / recentOpenHours;
        }

        // Theoretical M/G/1, only from measured inputs; the Overloaded flag is reported with no theoretical numbers.
        MG1Result theoretical;
        if (lambda is null)
        {
            theoretical = new MG1Result(TheoreticalStatus.InsufficientData, "No open time in the period.", null, null, null, null);
        }
        else if (meanMinutes is null)
        {
            theoretical = new MG1Result(TheoreticalStatus.InsufficientData, "No completed consultations with a valid service duration in the period.", null, null, null, null);
        }
        else
        {
            var varianceHours2 = (varianceMinutes ?? 0) / 3600.0;
            theoretical = MG1Model.Compute(lambda.Value, meanMinutes.Value / 60.0, varianceHours2, serviceMinutes.Length);
        }

        // Kingman G/G/1 next to M/G/1: the same inputs plus the measured arrival variability.
        var kingman = theoretical.Status == TheoreticalStatus.Computed && arrivalCa2 is { } ca2 && lambda is { } kingmanLambda
            ? KingmanModel.Compute(kingmanLambda, meanMinutes!.Value / 60.0, (varianceMinutes ?? 0) / 3600.0, ca2)
            : null;

        // Priority M/G/1 (booked patients called before walk-ins): the same mean and variance, split by arrival source.
        // Both sources together are every counted attempt, so the two rates add up to lambda.
        double? priorityWqScheduled = null;
        double? priorityWqWalkIn = null;
        if (theoretical.Status == TheoreticalStatus.Computed && openHours > 0 && meanMinutes is { } priorityMean)
        {
            var priorityScheduledPerHour = counted.Count(item => item.Source == QueueEntrySource.Scheduled) / openHours;
            var priorityWalkInPerHour = counted.Count(item => item.Source == QueueEntrySource.WalkIn) / openHours;
            var priority = PriorityQueueModel.Compute(priorityScheduledPerHour, priorityWalkInPerHour, priorityMean / 60.0, (varianceMinutes ?? 0) / 3600.0);
            if (priority is { } waits)
            {
                priorityWqScheduled = waits.WqScheduledHours * 60.0;
                priorityWqWalkIn = waits.WqWalkInHours * 60.0;
            }
        }

        var appointmentsReached = appointments.Where(item =>
            !item.ClinicUnavailable &&
            item.AppointmentDate >= request.FromDate &&
            item.AppointmentDate <= request.ToDate &&
            item.AppointmentDate <= request.Today &&
            // Only appointments with a final attendance outcome: never-confirmed (Pending) requests and
            // Confirmed appointments that have not yet resolved would otherwise dilute the rate.
            item.Status is AppointmentStatus.Completed or AppointmentStatus.NoShow).ToArray();
        var noShowDenominator = appointmentsReached.Length;
        var noShowCount = appointmentsReached.Count(item => item.Status == AppointmentStatus.NoShow);
        double? noShowRate = noShowDenominator > 0 ? (double)noShowCount / noShowDenominator : null;

        var walkInCompleted = counted.Count(item => item.Source == QueueEntrySource.WalkIn && item.Status == QueueEntryStatus.Completed);
        double? walkInPerHour = openHours > 0 ? walkInCompleted / openHours : null;

        var metrics = new DoctorQueueingMetrics(
            ArrivalsCount: counted.Length,
            ServedCount: served.Count,
            ServiceSampleCount: serviceSample.Length,
            ExcludedClinicUnavailable: clinicUnavailable,
            ExcludedInvalidTimestamps: invalidTimestamps,
            ExcludedServiceAtOrAboveCap: excludedAboveCap,
            OpenHours: openHours,
            LambdaPerHour: lambda,
            MeanServiceMinutes: meanMinutes,
            ServiceStdDevMinutes: stdDevMinutes,
            MuPerHour: mu,
            Rho: rho,
            Overloaded: overloaded,
            MeasuredServiceHours: serviceHours,
            Utilization: utilization,
            MeasuredWqMinutes: wqMinutes,
            MeasuredWsMinutes: wsMinutes,
            MeasuredLq: measuredLq,
            MeasuredLs: measuredLs,
            LittleLq: littleLq,
            LittleLs: littleLs,
            LittleLqCheckPercent: lqCheck,
            LittleLsCheckPercent: lsCheck,
            TheoreticalStatus: theoretical.Status,
            TheoreticalReason: theoretical.Reason,
            TheoreticalLq: theoretical.Lq,
            TheoreticalWqMinutes: theoretical.WqHours * 60.0,
            TheoreticalWsMinutes: theoretical.WsHours * 60.0,
            TheoreticalLs: theoretical.Ls,
            NoShowCount: noShowCount,
            NoShowDenominator: noShowDenominator,
            NoShowRate: noShowRate,
            WalkInCompletedCount: walkInCompleted,
            WalkInPerHour: walkInPerHour,
            NotSeenCount: notSeen,
            NotSeenRate: notSeenRate,
            ArrivalsOutsideOpenTime: arrivalsOutsideOpen,
            ServedScheduledCount: servedScheduled.Length,
            MeasuredWqScheduledMinutes: wqScheduled,
            ServedWalkInCount: servedWalkIn.Length,
            MeasuredWqWalkInMinutes: wqWalkIn,
            DelayPastAppointmentCount: delays.Length,
            DelayPastAppointmentMinutes: delayPastSlot,
            TheoreticalNearSaturation: theoretical.NearSaturation,
            ScheduledOpenHours: scheduledOpenHours,
            ArrivalVariabilityCa2: arrivalCa2,
            InterarrivalSampleCount: gaps.Length,
            KingmanWqMinutes: kingman?.WqHours * 60.0,
            KingmanLq: kingman?.Lq,
            RecentArrivalsPerHour: recentArrivalsPerHour,
            MeanServiceUsesHistory: prior is not null,
            WaitingRoomAverage: waitingRoomAverage,
            InClinicAverage: inClinicAverage,
            LeftAfterWaitingCount: leavers.Length,
            MeanWaitBeforeLeavingMinutes: meanWaitBeforeLeaving,
            PriorityWqScheduledMinutes: priorityWqScheduled,
            PriorityWqWalkInMinutes: priorityWqWalkIn);

        var hourly = BuildHourlyProfile(counted, appointments, served, open, meanMinutes / 60.0, request);
        // Historical only: live means are blended with history and are partial, so they must not drive advice.
        var slotAdvice = request.CutoffUtc is null
            ? SlotLengthAdvice.Evaluate(doctor.AppointmentDurationMinutes, metrics.MeanServiceMinutes, metrics.ServiceSampleCount)
            : null;
        // Nothing to advise when the suggested length is already scheduled to take effect.
        if (slotAdvice is not null && doctor.NextAppointmentDurationMinutes == slotAdvice.SuggestedSlotMinutes)
            slotAdvice = null;

        return new DoctorAnalytics(doctor.DoctorId, doctor.FullName, request.FromDate, request.ToDate, metrics, hourly, slotAdvice);
    }

    /// <summary>Hourly profile in clinic-local hours. Minutes are attributed per one-minute segment by the segment start instant.</summary>
    private static IReadOnlyList<HourBucket> BuildHourlyProfile(
        IReadOnlyList<AnalyticsVisitRow> counted,
        IReadOnlyList<AnalyticsAppointmentRow> appointments,
        IReadOnlyList<ServedObservation> served,
        IReadOnlyList<OpenInterval> open,
        double? meanServiceHours,
        AnalyticsRequest request)
    {
        var scheduled = new int[24];
        foreach (var item in appointments.Where(item =>
                     !item.ClinicUnavailable &&
                     item.AppointmentDate >= request.FromDate &&
                     item.AppointmentDate <= request.ToDate &&
                     item.AppointmentDate <= request.Today &&
                     item.Status is AppointmentStatus.Confirmed or AppointmentStatus.Completed or AppointmentStatus.NoShow))
        {
            scheduled[item.StartTime.Hour]++;
        }

        var arrivals = new int[24];
        foreach (var visit in counted)
            arrivals[LocalHour(visit.ArrivedAt, request.ClinicZone)]++;

        var busy = new double[24];
        foreach (var item in served)
            AddMinutes(busy, item.Started, item.Started + CappedService(item, request.ServiceCap), request.ClinicZone);

        var openMinutes = new double[24];
        foreach (var interval in open)
            AddMinutes(openMinutes, interval.Start, interval.End, request.ClinicZone);

        return Enumerable.Range(0, 24)
            .Select(hour => new HourBucket(
                hour,
                scheduled[hour],
                arrivals[hour],
                busy[hour],
                openMinutes[hour],
                openMinutes[hour] > 0 ? busy[hour] / openMinutes[hour] : null,
                // Workload of the hour: arrivals per open hour times the mean consultation (rho for that hour).
                openMinutes[hour] > 0 && meanServiceHours is { } meanHours ? arrivals[hour] / (openMinutes[hour] / 60.0) * meanHours : null))
            .ToArray();
    }

    private static void AddMinutes(double[] buckets, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var cursor = start;
        var minute = TimeSpan.FromMinutes(1);
        while (cursor < end)
        {
            var remaining = end - cursor;
            var segment = remaining < minute ? remaining : minute;
            buckets[LocalHour(cursor, zone)] += segment.TotalMinutes;
            cursor += segment;
        }
    }

    /// <summary>
    /// Per doctor, the mean and size of the valid consultation sample (0 &lt; S &lt; cap) in the history rows, for the
    /// live blend. Doctors with no valid consultation are left out.
    /// </summary>
    public static IReadOnlyDictionary<Guid, ServicePrior> BuildServicePriors(IEnumerable<AnalyticsServiceHistoryRow> rows, TimeSpan serviceCap) =>
        rows
            .Select(row => (row.DoctorId, Minutes: (row.CompletedAt - row.StartedAt).TotalMinutes))
            .Where(item => item.Minutes > 0 && item.Minutes < serviceCap.TotalMinutes)
            .GroupBy(item => item.DoctorId)
            .ToDictionary(group => group.Key, group => new ServicePrior(group.Average(item => item.Minutes), group.Count()));

    private static TimeSpan Overlap(OpenInterval interval, DateTimeOffset from, DateTimeOffset to)
    {
        var start = interval.Start > from ? interval.Start : from;
        var end = interval.End < to ? interval.End : to;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static TimeSpan CappedService(ServedObservation item, TimeSpan cap) =>
        item.Service < cap ? item.Service : cap;

    /// <summary>Wait after the booked start, or after arrival when the patient came late; zero when seen early.</summary>
    private static double DelayPastSlotMinutes(ServedObservation item)
    {
        var from = item.Arrived > item.Slot!.Value ? item.Arrived : item.Slot.Value;
        return Math.Max(0, (item.Started - from).TotalMinutes);
    }

    private static bool IsInside(DateTimeOffset instant, IReadOnlyList<OpenInterval> open) =>
        open.Any(interval => interval.Start <= instant && instant < interval.End);

    private static int LocalHour(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).Hour;

    private static double? CheckPercent(double? measured, double? expected)
    {
        if (measured is null || expected is null)
            return null;
        if (expected.Value == 0)
            return measured.Value == 0 ? 0 : null;
        return Math.Abs(measured.Value - expected.Value) / expected.Value * 100.0;
    }

    private static T[] Get<T>(Dictionary<Guid, T[]> map, Guid doctorId) =>
        map.TryGetValue(doctorId, out var value) ? value : [];
}
