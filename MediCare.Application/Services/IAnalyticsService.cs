using MediCare.Application.Analytics;

namespace MediCare.Application.Services;

/// <summary>
/// Operational analytics. Historical periods must end before the clinic-local today. Live results describe today so far
/// and are never cached.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>One doctor's historical metrics, or null when the doctor does not exist.</summary>
    Task<DoctorAnalytics?> GetDoctorAnalyticsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>The signed-in doctor's own historical metrics. Throws UnauthorizedAccessException without a linked doctor profile.</summary>
    Task<DoctorAnalytics> GetOwnDoctorAnalyticsAsync(Guid applicationUserId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>Per-doctor rows and clinic totals. No clinic-level M/G/1: each doctor is a separate server.</summary>
    Task<ClinicAnalyticsReport> GetClinicAnalyticsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>Today so far, for the given doctors (all active doctors when null).</summary>
    Task<LiveOperationsReport> GetLiveAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken);
}

public sealed record ClinicTotals(
    int ArrivalsCount,
    int ServedCount,
    double OpenHours,
    double? Utilization,
    int NoShowCount,
    int NoShowDenominator,
    double? NoShowRate,
    int WalkInCompletedCount,
    double? WalkInPerHour);

public sealed record ClinicAnalyticsReport(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<DoctorAnalytics> Rows,
    ClinicTotals Totals);

public sealed record LiveDoctorRow(
    Guid DoctorId,
    string DoctorName,
    int WaitingNow,
    int CalledNow,
    int InConsultation,
    double? LongestCurrentWaitMinutes,
    DoctorQueueingMetrics MetricsSoFar);

public sealed record LiveOperationsReport(
    DateOnly Today,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<LiveDoctorRow> Rows);
