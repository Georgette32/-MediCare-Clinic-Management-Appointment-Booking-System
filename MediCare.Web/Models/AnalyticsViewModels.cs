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

    public static string Minutes(double? value) => value is null ? "-" : $"{value.Value:0.0} min";

    public static string Patients(double? value) => value is null ? "-" : $"{value.Value:0.00}";

    public static string PerHour(double? value) => value is null ? "-" : $"{value.Value:0.00} /h";

    public static string Percent(double? fraction) => fraction is null ? "-" : $"{fraction.Value * 100:0.0}%";

    public static string Count(double? value) => value is null ? "-" : $"{value.Value:0.0}";

    public static string LittleCheck(double? percent) => percent is null ? "-" : $"{percent.Value:0.0}% difference";
}
