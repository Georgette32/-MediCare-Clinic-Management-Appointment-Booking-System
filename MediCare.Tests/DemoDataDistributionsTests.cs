using MediCare.Infrastructure.DemoData;

namespace MediCare.Tests;

/// <summary>The demo history samplers, checked on large fixed-seed samples so the statistics are stable run to run.</summary>
public sealed class DemoDataDistributionsTests
{
    private const int Samples = 200_000;

    [Fact]
    public void Lognormal_draws_keep_the_requested_mean_and_spread()
    {
        const double mean = 21.6; // a 20-minute slot at a 1.08 factor
        const double cv = 0.45;
        var rng = new Random(101);
        var values = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.LogNormalSample(rng, mean, cv)).ToArray();

        var sampleMean = values.Average();
        var sampleSd = Math.Sqrt(values.Sum(value => (value - sampleMean) * (value - sampleMean)) / (values.Length - 1));

        Assert.InRange(sampleMean / mean, 0.97, 1.03);
        Assert.InRange(sampleSd / sampleMean, cv - 0.05, cv + 0.05);
        Assert.All(values, value => Assert.True(value > 0));
    }

    [Fact]
    public void Lognormal_draws_are_right_skewed()
    {
        var rng = new Random(202);
        var values = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.LogNormalSample(rng, 20, 0.5)).OrderBy(value => value).ToArray();

        var median = values[values.Length / 2];
        var mean = values.Average();
        Assert.True(mean > median, "A lognormal sample has its mean above its median.");
    }

    [Fact]
    public void Normal_draws_are_standard()
    {
        var rng = new Random(303);
        var values = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.NormalSample(rng, 0, 1)).ToArray();

        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1));
        Assert.InRange(mean, -0.01, 0.01);
        Assert.InRange(sd, 0.99, 1.01);
    }

    [Fact]
    public void Poisson_draws_keep_the_requested_mean()
    {
        const double lambda = 3.5;
        var rng = new Random(404);
        var values = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.PoissonSample(rng, lambda)).ToArray();

        Assert.InRange(values.Average() / lambda, 0.97, 1.03);
        Assert.All(values, value => Assert.True(value >= 0));
    }

    [Fact]
    public void Poisson_with_zero_rate_is_always_zero()
    {
        var rng = new Random(505);

        Assert.All(Enumerable.Range(0, 1_000), _ => Assert.Equal(0, DemoDistributions.PoissonSample(rng, 0)));
    }

    [Fact]
    public void Exponential_draws_keep_the_requested_mean()
    {
        var rng = new Random(606);
        var values = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.ExponentialSample(rng, 25)).ToArray();

        Assert.InRange(values.Average() / 25.0, 0.97, 1.03);
    }

    [Fact]
    public void Late_arrival_tail_is_about_twelve_percent_and_the_mean_offset_matches()
    {
        var rng = new Random(707);
        var samples = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.ArrivalOffset(rng, 60)).ToArray();

        var lateShare = samples.Count(sample => sample.LateTail) / (double)samples.Length;
        var meanOffset = samples.Average(sample => sample.Minutes);

        Assert.InRange(lateShare, 0.11, 0.13);
        // Normal(-8, 9) plus 0.12 × 20 minutes on average from the tail.
        Assert.InRange(meanOffset, -6.1, -5.1);
    }

    [Fact]
    public void Arrival_offsets_stay_within_the_early_limit_and_the_grace_period()
    {
        var rng = new Random(808);
        var samples = Enumerable.Range(0, Samples).Select(_ => DemoDistributions.ArrivalOffset(rng, 20)).ToArray();

        Assert.All(samples, sample =>
        {
            Assert.True(sample.Minutes >= -45);
            Assert.True(sample.Minutes <= 15, $"Arrival {sample.Minutes} minutes after the slot is past a 20-minute grace period.");
        });
    }

    [Fact]
    public void Demand_is_highest_in_the_first_hour_and_on_weekends()
    {
        var saturdayFirstHour = DemoDistributions.PastDayFill(DayOfWeek.Saturday, 10 * 60, 14 * 60, 10 * 60);
        var saturdayMiddle = DemoDistributions.PastDayFill(DayOfWeek.Saturday, 10 * 60, 14 * 60, 11 * 60 + 30);
        var thursdayMiddle = DemoDistributions.PastDayFill(DayOfWeek.Thursday, 10 * 60, 14 * 60, 11 * 60 + 30);

        Assert.True(saturdayFirstHour > saturdayMiddle);
        Assert.True(saturdayMiddle > thursdayMiddle);
    }

    [Fact]
    public void Demand_matches_the_documented_factors_and_is_capped()
    {
        // Monday, middle of a morning window: 0.72 × 1.0 × 0.9.
        Assert.Equal(0.72 * 1.0 * 0.9, DemoDistributions.PastDayFill(DayOfWeek.Monday, 9 * 60, 13 * 60, 10 * 60 + 30), 6);
        // Thursday, last hour: 0.72 × 0.85 × 1.0.
        Assert.Equal(0.72 * 0.85 * 1.0, DemoDistributions.PastDayFill(DayOfWeek.Thursday, 9 * 60, 13 * 60, 12 * 60), 6);
        // Saturday evening, first hour: 0.72 × 1.15 × 1.15 × 1.1 is above the cap.
        Assert.Equal(0.97, DemoDistributions.PastDayFill(DayOfWeek.Saturday, 18 * 60, 22 * 60, 18 * 60));
    }

    [Fact]
    public void Same_seed_gives_the_same_draws()
    {
        var first = new Random(909);
        var second = new Random(909);

        for (var index = 0; index < 100; index++)
        {
            Assert.Equal(DemoDistributions.LogNormalSample(first, 20, 0.45), DemoDistributions.LogNormalSample(second, 20, 0.45));
            Assert.Equal(DemoDistributions.PoissonSample(first, 2), DemoDistributions.PoissonSample(second, 2));
        }
    }
}
