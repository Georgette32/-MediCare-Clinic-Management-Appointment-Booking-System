using MediCare.Application.Abstractions;

namespace MediCare.Application.Analytics;

/// <summary>Raw rows for a set of doctors and a period. Loaded once per period by the service.</summary>
public sealed record AnalyticsSnapshot(
    IReadOnlyList<AnalyticsDoctor> Doctors,
    IReadOnlyList<AnalyticsVisitRow> Visits,
    IReadOnlyList<AnalyticsAppointmentRow> Appointments,
    IReadOnlyList<AnalyticsWorkingHourRow> WorkingHours,
    IReadOnlyList<AnalyticsHolidayRow> Holidays,
    IReadOnlyList<AnalyticsExtensionRow> Extensions,
    IReadOnlyList<AnalyticsBreakRow> Breaks);

/// <summary>
/// Parameters of one calculation. FromDate and ToDate are inclusive clinic-local dates. CutoffUtc is set only for live
/// results (open time is clipped to "now"). Today is the clinic-local date, used for the no-show "reached its day" rule.
/// </summary>
public sealed record AnalyticsRequest(
    DateOnly FromDate,
    DateOnly ToDate,
    DateOnly Today,
    DateTimeOffset? CutoffUtc,
    TimeSpan ServiceCap,
    TimeZoneInfo ClinicZone,
    Func<DateOnly, TimeOnly, DateTimeOffset> ToUtc);

/// <summary>
/// Measured and theoretical queueing metrics for one doctor (a single server) over the open time T.
/// Time values are minutes and rates are per hour; theoretical Lq and Ls are patient counts.
/// </summary>
public sealed record DoctorQueueingMetrics(
    int ArrivalsCount,
    int ServedCount,
    int ServiceSampleCount,
    int ExcludedClinicUnavailable,
    int ExcludedInvalidTimestamps,
    int ExcludedServiceAtOrAboveCap,
    double OpenHours,
    double? LambdaPerHour,
    double? MeanServiceMinutes,
    double? ServiceStdDevMinutes,
    double? MuPerHour,
    double? Rho,
    bool Overloaded,
    double? MeasuredServiceHours,
    double? Utilization,
    double? MeasuredWqMinutes,
    double? MeasuredWsMinutes,
    double? MeasuredLq,
    double? MeasuredLs,
    double? LittleLq,
    double? LittleLs,
    double? LittleLqCheckPercent,
    double? LittleLsCheckPercent,
    TheoreticalStatus TheoreticalStatus,
    string? TheoreticalReason,
    double? TheoreticalLq,
    double? TheoreticalWqMinutes,
    double? TheoreticalWsMinutes,
    double? TheoreticalLs,
    int NoShowCount,
    int NoShowDenominator,
    double? NoShowRate,
    int WalkInCompletedCount,
    double? WalkInPerHour);

/// <summary>One clinic-local hour of the day, summed over the period.</summary>
public sealed record HourBucket(
    int Hour,
    int ScheduledStarts,
    int ActualArrivals,
    double BusyMinutes,
    double OpenMinutes,
    double? Utilization);

public sealed record DoctorAnalytics(
    Guid DoctorId,
    string DoctorName,
    DateOnly FromDate,
    DateOnly ToDate,
    DoctorQueueingMetrics Metrics,
    IReadOnlyList<HourBucket> Hours);

/// <summary>A served observation with valid timestamps: arrived &lt;= started &lt; completed.</summary>
internal readonly record struct ServedObservation(DateTimeOffset Arrived, DateTimeOffset Started, DateTimeOffset Completed)
{
    public TimeSpan Service => Completed - Started;
    public TimeSpan Wait => Started - Arrived;
    public TimeSpan Sojourn => Completed - Arrived;
}
