using MediCare.Application.Analytics;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

/// <summary>Shared view model for the analytics pages. Exactly one of the report properties is set per page.</summary>
public sealed record AnalyticsPageViewModel
{
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public ClinicAnalyticsReport? Clinic { get; init; }
    public DoctorAnalytics? Doctor { get; init; }
    public LiveOperationsReport? Live { get; init; }
    public bool IsOwnStatistics { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? SelectedDoctorId { get; init; }
    public IReadOnlyList<DoctorChoice> Doctors { get; init; } = [];
    public TimeZoneInfo ClinicZone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>The last 30 completed clinic days, ending yesterday (clinic-local).</summary>
    public static (DateOnly From, DateOnly To) DefaultHistoricalRange(DateOnly clinicToday)
    {
        var to = clinicToday.AddDays(-1);
        return (to.AddDays(-29), to);
    }

    /// <summary>Below this many observations an average is shown with a "small sample" note.</summary>
    public const int SmallSampleSize = 20;

    public static bool IsSmallSample(int count) => count is > 0 and < SmallSampleSize;

    public static string Minutes(double? value) => value is null ? "-" : $"{value.Value:0} min";

    /// <summary>A spread in minutes, e.g. "± 8 min" for a standard deviation.</summary>
    public static string PlusMinusMinutes(double? value) => value is null ? "-" : $"± {value.Value:0} min";

    /// <summary>An average head-count, e.g. "1.4 patients".</summary>
    public static string Patients(double? value) => value is null ? "-" : $"{value.Value:0.0} patients";

    public static string PerHour(double? value) => value is null ? "-" : $"{value.Value:0.0} per hour";

    public static string Percent(double? fraction) => fraction is null ? "-" : $"{fraction.Value * 100:0}%";

    public static string Count(double? value) => value is null ? "-" : $"{value.Value:0.0}";

    /// <summary>How the two ways of measuring the same average agree; small differences are normal.</summary>
    public static string LittleCheck(double? percent) => percent is null ? "-" : $"{percent.Value:0}% apart";

    /// <summary>
    /// Demand against capacity: patients arriving per hour divided by patients the doctor can see per hour, as a
    /// percentage. 100% or more means patients arrive faster than they can be seen.
    /// </summary>
    public static string Workload(double? demandOverCapacity) =>
        demandOverCapacity is null ? "-" : $"{demandOverCapacity.Value * 100:0}% of capacity";

    /// <summary>The width (0-100) of a workload bar, capped so an overload still fits.</summary>
    public static int WorkloadBar(double? demandOverCapacity) =>
        demandOverCapacity is null ? 0 : (int)Math.Clamp(demandOverCapacity.Value * 80, 0, 100);
}
