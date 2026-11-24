namespace MediCare.Application.Common;

/// <summary>
/// Expected-duration constants and tuning. The minimum consultation sample size is clinic data (ClinicSettings),
/// read through IClinicSettingsProvider, so it is not configured here.
/// </summary>
public sealed class ExpectedDurationOptions
{
    public const int DefaultMaxDurationMinutesCap = 60;
    public const int TrailingDaysWindow = 30;
    public const int GracePeriodMultiplier = 3;

    public int MaxDurationMinutesCap { get; set; } = DefaultMaxDurationMinutesCap;
}
