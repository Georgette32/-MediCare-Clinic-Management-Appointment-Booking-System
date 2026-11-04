using MediCare.Application;
using MediCare.Domain;

namespace MediCare.Tests;

public sealed class SchedulingRulesTests
{
    [Theory]
    [InlineData(10, 20)]
    [InlineData(15, 30)]
    [InlineData(20, 40)]
    [InlineData(30, 60)]
    public void Grace_period_is_twice_doctors_duration(int duration, int expected) =>
        Assert.Equal(expected, SchedulingRules.GracePeriodMinutes(duration));

    [Fact]
    public void Arrival_exactly_on_grace_boundary_remains_eligible()
    {
        var scheduled = DateTimeOffset.Parse("2026-11-03T10:00:00Z");
        Assert.True(SchedulingRules.IsWithinGrace(scheduled, scheduled.AddMinutes(30), 15));
        Assert.False(SchedulingRules.IsWithinGrace(scheduled, scheduled.AddMinutes(30).AddTicks(1), 15));
    }

    [Fact]
    public void Statistics_use_sample_variance_over_latest_thirty_values()
    {
        var stats = ConsultationStatistics.Calculate([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30)]);
        Assert.Equal(3, stats.Samples);
        Assert.Equal(20, stats.MeanMinutes, 5);
        Assert.Equal(100, stats.SampleVariance, 5);
        Assert.Equal(10, stats.StandardDeviation, 5);
    }

    [Fact]
    public void Insufficient_history_uses_configured_default_duration()
    {
        var settings = new DoctorSchedulingSettings { DefaultConsultationDurationMinutes = 18, MinimumHistorySamples = 5 };
        var stats = ConsultationStatistics.Calculate([TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(80)]);
        Assert.Equal(18, SchedulingRules.EffectiveSlotMinutes(settings, stats));
    }

    [Fact]
    public void Sufficient_history_adds_configurable_variability_buffer()
    {
        var settings = new DoctorSchedulingSettings { MinimumHistorySamples = 2, SafetyBufferMultiplier = 0.5m };
        var stats = ConsultationStatistics.Calculate([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)]);
        Assert.Equal(28, SchedulingRules.EffectiveSlotMinutes(settings, stats));
    }

    [Fact]
    public void Queue_policy_keeps_booked_priority_and_walk_in_fifo()
    {
        var earlyBooked = new QueueEntry { Type = QueueType.Booked, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow, Appointment = new Appointment { ScheduledStart = DateTimeOffset.UtcNow.AddHours(1) } };
        var laterBooked = new QueueEntry { Type = QueueType.Booked, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow.AddMinutes(-5), Appointment = new Appointment { ScheduledStart = DateTimeOffset.UtcNow.AddHours(2) } };
        var firstWalkIn = new QueueEntry { Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var secondWalkIn = new QueueEntry { Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow.AddMinutes(-2) };
        var pendingWalkIn = new QueueEntry { Type = QueueType.WalkIn, Approval = WalkInApproval.Pending, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow.AddMinutes(-20) };

        Assert.Equal(new[] { earlyBooked, laterBooked, firstWalkIn, secondWalkIn }, QueuePolicy.OrderEligible([secondWalkIn, pendingWalkIn, laterBooked, firstWalkIn, earlyBooked]).ToArray());
    }

    [Fact]
    public void Queue_estimate_accounts_for_remaining_consultation_and_scheduled_start()
    {
        var now = DateTimeOffset.Parse("2026-11-03T10:00:00Z");
        var active = new QueueEntry { EnteredConsultationAt = now.AddMinutes(-5), Status = QueueStatus.InConsultation };
        var called = new QueueEntry { Id = 1, PatientId = "called", Type = QueueType.WalkIn, CalledAt = now };
        var booked = new QueueEntry { Id = 2, PatientId = "booked", Type = QueueType.Booked, Status = QueueStatus.Waiting, Appointment = new Appointment { ScheduledStart = now.AddMinutes(50) } };
        var estimates = QueueEstimateCalculator.Estimate(now, 20, active, [called], [booked]);
        Assert.Equal(now.AddMinutes(15), estimates[0].EstimatedStartAt);
        Assert.Equal(now.AddMinutes(50), estimates[1].EstimatedStartAt);
        Assert.Equal(50, estimates[1].EstimatedWaitMinutes);
    }
}
