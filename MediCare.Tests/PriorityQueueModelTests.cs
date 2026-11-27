using MediCare.Application.Analytics;

namespace MediCare.Tests;

/// <summary>Checks for the non-preemptive priority M/G/1 model (booked patients before walk-ins).</summary>
public sealed class PriorityQueueModelTests
{
    [Theory]
    [InlineData(1.5, 0.5, 1.0 / 3.0, 1.0 / 36.0)]
    [InlineData(2.0, 0.4, 0.25, 0.01)]
    [InlineData(0.5, 0.5, 0.2, 0.0)]
    [InlineData(0.1, 3.0, 0.2, 0.05)]
    public void Conservation_law_matches_fcfs_pollaczek_khinchine(double scheduled, double walkIn, double mean, double variance)
    {
        var result = PriorityQueueModel.Compute(scheduled, walkIn, mean, variance);
        Assert.NotNull(result);
        var (wq1, wq2) = result!.Value;

        var weightedAverage = (scheduled * wq1 + walkIn * wq2) / (scheduled + walkIn);
        var fcfs = MG1Model.Compute(scheduled + walkIn, mean, variance, 10).WqHours!.Value;

        Assert.Equal(fcfs, weightedAverage, 9);
    }

    [Theory]
    [InlineData(2.0, 1.0 / 3.0, 1.0 / 36.0)]
    [InlineData(1.2, 0.25, 0.04)]
    public void Without_walk_ins_booked_wait_equals_pollaczek_khinchine(double scheduled, double mean, double variance)
    {
        var result = PriorityQueueModel.Compute(scheduled, 0, mean, variance);
        Assert.NotNull(result);

        var expected = MG1Model.Compute(scheduled, mean, variance, 10).WqHours!.Value;
        Assert.Equal(expected, result!.Value.WqScheduledHours, 9);
    }

    [Theory]
    [InlineData(1.5, 0.5, 1.0 / 3.0, 1.0 / 36.0)]
    [InlineData(0.2, 0.1, 0.25, 0.0)]
    [InlineData(1.0, 1.0, 0.25, 0.0)]
    public void Walk_ins_wait_longer_than_booked_patients(double scheduled, double walkIn, double mean, double variance)
    {
        var result = PriorityQueueModel.Compute(scheduled, walkIn, mean, variance);
        Assert.NotNull(result);

        Assert.True(result!.Value.WqWalkInHours > result.Value.WqScheduledHours);
    }

    [Fact]
    public void Hand_computed_literal_with_deterministic_service()
    {
        // E[S^2] = 1/16, W0 = 2 * (1/16) / 2 = 1/16, rho1 = 1/4, rho = 1/2.
        // Wq1 = (1/16) / (3/4) = 1/12 h, Wq2 = (1/16) / ((3/4)(1/2)) = 1/6 h.
        var result = PriorityQueueModel.Compute(1.0, 1.0, 0.25, 0.0);
        Assert.NotNull(result);

        Assert.Equal(1.0 / 12.0, result!.Value.WqScheduledHours, 12);
        Assert.Equal(1.0 / 6.0, result.Value.WqWalkInHours, 12);
    }

    [Fact]
    public void Returns_null_when_total_utilisation_reaches_one()
    {
        // rho = (2 + 2) * 0.25 = 1: no steady state.
        Assert.Null(PriorityQueueModel.Compute(2.0, 2.0, 0.25, 0.0));
    }

    [Fact]
    public void Returns_null_when_scheduled_utilisation_alone_reaches_one()
    {
        // rho1 = 4 * 0.25 = 1 even with no walk-ins.
        Assert.Null(PriorityQueueModel.Compute(4.0, 0, 0.25, 0.0));
    }

    [Theory]
    [InlineData(-1.0, 0.5, 0.25, 0.0)]
    [InlineData(0.5, -1.0, 0.25, 0.0)]
    [InlineData(0.5, 0.5, 0.25, -0.01)]
    [InlineData(0.5, 0.5, 0.0, 0.0)]
    [InlineData(0.5, 0.5, -0.25, 0.0)]
    [InlineData(0.5, 0.5, double.NaN, 0.0)]
    [InlineData(double.NaN, 0.5, 0.25, 0.0)]
    [InlineData(0.5, 0.5, 0.25, double.NaN)]
    [InlineData(0.0, 0.0, 0.25, 0.0)]
    public void Returns_null_for_invalid_inputs_and_zero_arrivals(double scheduled, double walkIn, double mean, double variance)
    {
        Assert.Null(PriorityQueueModel.Compute(scheduled, walkIn, mean, variance));
    }
}
