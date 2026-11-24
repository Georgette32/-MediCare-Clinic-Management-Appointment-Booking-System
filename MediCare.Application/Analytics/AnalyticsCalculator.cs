using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Analytics;

/// <summary>
/// Pure queueing-analytics engine. Each doctor is an independent single server.
/// Measured values come from real timestamps. Theoretical M/G/1 values appear only when rho is below 1.
/// Exclusions: attempts tied to appointments cancelled with reason "clinic unavailable" are excluded from every
/// measure; completed attempts with invalid timestamps are excluded from service and waiting samples; service
/// durations at or above the cap are excluded from the service sample (mu, variance, utilization, busy minutes).
/// </summary>
public static class AnalyticsCalculator
{
    public static IReadOnlyList<DoctorAnalytics> Calculate(AnalyticsSnapshot snapshot, AnalyticsRequest request)
    {
        var visitsByDoctor = snapshot.Visits.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var appointmentsByDoctor = snapshot.Appointments.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var hoursByDoctor = snapshot.WorkingHours.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var holidaysByDoctor = snapshot.Holidays.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var extensionsByDoctor = snapshot.Extensions.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());
        var breaksByDoctor = snapshot.Breaks.GroupBy(item => item.DoctorId).ToDictionary(group => group.Key, group => group.ToArray());

        return snapshot.Doctors
            .Select(doctor => ComputeDoctor(
                doctor,
                Get(visitsByDoctor, doctor.DoctorId),
                Get(appointmentsByDoctor, doctor.DoctorId),
                Get(hoursByDoctor, doctor.DoctorId),
                Get(holidaysByDoctor, doctor.DoctorId),
                Get(extensionsByDoctor, doctor.DoctorId),
                Get(breaksByDoctor, doctor.DoctorId),
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
        AnalyticsRequest request)
    {
        var open = OpenTimeCalculator.Build(request.FromDate, request.ToDate, request.ToUtc, hours, holidays, extensions, breaks, request.CutoffUtc);
        var openHours = OpenTimeCalculator.TotalHours(open);

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
            served.Add(new ServedObservation(visit.ArrivedAt, started, completed));
        }

        // Exclusion 2: service durations at or above the cap. 0 < S always holds for a served observation (started < completed).
        var serviceSample = served.Where(item => item.Service < request.ServiceCap).ToArray();
        var excludedAboveCap = served.Count - serviceSample.Length;
        var serviceMinutes = serviceSample.Select(item => item.Service.TotalMinutes).ToArray();

        double? lambda = openHours > 0 ? counted.Length / openHours : null;
        double? meanMinutes = serviceMinutes.Length > 0 ? serviceMinutes.Average() : null;
        double? varianceMinutes = serviceMinutes.Length >= 2
            ? serviceMinutes.Sum(item => Math.Pow(item - meanMinutes!.Value, 2)) / (serviceMinutes.Length - 1)
            : null;
        double? stdDevMinutes = varianceMinutes is { } variance ? Math.Sqrt(variance) : null;
        double? mu = meanMinutes is { } mean ? 60.0 / mean : null;
        double? rho = lambda is { } l && mu is { } m ? l / m : null;
        var overloaded = rho is >= 1.0;

        double? serviceHours = serviceMinutes.Length > 0 ? serviceMinutes.Sum() / 60.0 : null;
        double? utilization = serviceHours is { } busy && openHours > 0 ? busy / openHours : null;

        // Measured waiting and system times over served observations (valid timestamps, including long consultations).
        double? wqMinutes = served.Count > 0 ? served.Average(item => item.Wait.TotalMinutes) : null;
        double? wsMinutes = served.Count > 0 ? served.Average(item => item.Sojourn.TotalMinutes) : null;

        // Time-average L from the event sweep over the open time.
        double? measuredLq = openHours > 0
            ? TimeAverageSweep.IntegrateHours(served.Select(item => (item.Arrived, item.Started)), open) / openHours
            : null;
        double? measuredLs = openHours > 0
            ? TimeAverageSweep.IntegrateHours(served.Select(item => (item.Arrived, item.Completed)), open) / openHours
            : null;

        // Little's Law: lambda * W with lambda = arrivals / T (as displayed), and the check against the served throughput,
        // because L and W are both taken over served observations.
        double? littleLq = lambda is { } lamArr && wqMinutes is { } wqA ? lamArr * (wqA / 60.0) : null;
        double? littleLs = lambda is { } lamArr2 && wsMinutes is { } wsA ? lamArr2 * (wsA / 60.0) : null;
        double? lqCheck = CheckPercent(measuredLq, openHours > 0 && wqMinutes is { } wq1 ? served.Count / openHours * (wq1 / 60.0) : null);
        double? lsCheck = CheckPercent(measuredLs, openHours > 0 && wsMinutes is { } ws1 ? served.Count / openHours * (ws1 / 60.0) : null);

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
            WalkInPerHour: walkInPerHour);

        var hourly = BuildHourlyProfile(counted, appointments, serviceSample, open, request);
        return new DoctorAnalytics(doctor.DoctorId, doctor.FullName, request.FromDate, request.ToDate, metrics, hourly);
    }

    /// <summary>Hourly profile in clinic-local hours. Minutes are attributed per one-minute segment by the segment start instant.</summary>
    private static IReadOnlyList<HourBucket> BuildHourlyProfile(
        IReadOnlyList<AnalyticsVisitRow> counted,
        IReadOnlyList<AnalyticsAppointmentRow> appointments,
        IReadOnlyList<ServedObservation> serviceSample,
        IReadOnlyList<OpenInterval> open,
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
        foreach (var item in serviceSample)
            AddMinutes(busy, item.Started, item.Completed, request.ClinicZone);

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
                openMinutes[hour] > 0 ? busy[hour] / openMinutes[hour] : null))
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
