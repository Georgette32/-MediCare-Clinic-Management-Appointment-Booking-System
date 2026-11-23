namespace MediCare.Application.Common;

public sealed class ExpectedDurationOptions
{
    public const int DefaultMaxDurationMinutesCap = 60;
    public const int TrailingDaysWindow = 30;
    public const int GracePeriodMultiplier = 3;

    public int? MinimumSampleSize { get; set; }
    public int MaxDurationMinutesCap { get; set; } = DefaultMaxDurationMinutesCap;
}
