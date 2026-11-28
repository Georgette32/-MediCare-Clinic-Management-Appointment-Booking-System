using MediCare.Application.Analytics;

namespace MediCare.Tests;

/// <summary>The slot-length rule: round the measured mean to 5 minutes, advise only on a real mismatch with enough data.</summary>
public sealed class SlotLengthAdviceTests
{
    [Fact]
    public void Mismatch_with_enough_data_suggests_the_rounded_mean()
    {
        var advice = SlotLengthAdvice.Evaluate(30, 33.0, 40);

        Assert.NotNull(advice);
        Assert.Equal(30, advice.CurrentSlotMinutes);
        Assert.Equal(35, advice.SuggestedSlotMinutes);
        Assert.Equal(33.0, advice.MeanServiceMinutes);
        Assert.Equal(40, advice.SampleCount);
    }

    [Fact]
    public void Mean_that_rounds_to_the_current_slot_gives_no_advice()
    {
        Assert.Null(SlotLengthAdvice.Evaluate(20, 21.6, 40));
    }

    [Fact]
    public void Mean_is_rounded_half_up_not_to_even()
    {
        // 22.5 rounds to 25 (half up). Banker's rounding would give 20, the current slot, and no advice.
        var advice = SlotLengthAdvice.Evaluate(20, 22.5, 40);

        Assert.NotNull(advice);
        Assert.Equal(25, advice.SuggestedSlotMinutes);
    }

    [Fact]
    public void Mismatch_suggests_the_rounded_longer_slot()
    {
        var advice = SlotLengthAdvice.Evaluate(20, 26.0, 40);

        Assert.NotNull(advice);
        Assert.Equal(25, advice.SuggestedSlotMinutes);
    }

    [Fact]
    public void Small_sample_gives_no_advice()
    {
        Assert.Null(SlotLengthAdvice.Evaluate(30, 33.0, 19));
    }

    [Fact]
    public void Exactly_the_minimum_sample_is_enough()
    {
        Assert.Equal(20, SlotLengthAdvice.MinimumSample);
        Assert.NotNull(SlotLengthAdvice.Evaluate(30, 33.0, SlotLengthAdvice.MinimumSample));
    }

    [Fact]
    public void Missing_mean_gives_no_advice()
    {
        Assert.Null(SlotLengthAdvice.Evaluate(20, null, 40));
    }

    [Fact]
    public void Suggestion_never_goes_below_five_minutes()
    {
        var advice = SlotLengthAdvice.Evaluate(10, 3.0, 40);

        Assert.NotNull(advice);
        Assert.Equal(5, advice.SuggestedSlotMinutes);
    }
}
