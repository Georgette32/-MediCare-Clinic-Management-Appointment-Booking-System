namespace MediCare.Application.Analytics;

/// <summary>
/// Advice to change a doctor's booking slot length when the measured average consultation no longer matches it.
/// The rule (see docs/analysis/slot_length_simulation.py): a slot far from the real average adds overtime, while a slot
/// within a couple of minutes of it makes no difference. The suggested slot is the measured mean rounded to the nearest
/// 5 minutes, with a minimum of 5. Advice only: nothing changes automatically.
/// </summary>
public sealed record SlotLengthAdvice(int CurrentSlotMinutes, int SuggestedSlotMinutes, double MeanServiceMinutes, int SampleCount)
{
    /// <summary>Fewer consultations than this and the mean is too noisy to act on.</summary>
    public const int MinimumSample = 20;

    /// <summary>
    /// Null when there is no advice: no measured mean, too small a sample, or the suggested slot already equals the
    /// current one.
    /// </summary>
    public static SlotLengthAdvice? Evaluate(int currentSlotMinutes, double? meanServiceMinutes, int sampleCount)
    {
        if (meanServiceMinutes is not { } mean || sampleCount < MinimumSample)
            return null;

        // Half up to the nearest 5 minutes. Math.Round would use banker's rounding (22.5 to 20), so it is not used here.
        var suggested = Math.Max(5, (int)Math.Floor(mean / 5.0 + 0.5) * 5);
        return suggested == currentSlotMinutes ? null : new SlotLengthAdvice(currentSlotMinutes, suggested, mean, sampleCount);
    }
}
