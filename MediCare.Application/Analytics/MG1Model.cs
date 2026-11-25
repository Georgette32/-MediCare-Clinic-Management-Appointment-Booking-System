namespace MediCare.Application.Analytics;

public enum TheoreticalStatus
{
    /// <summary>Steady-state values were computed (rho below 1 and enough service observations).</summary>
    Computed,
    /// <summary>rho is at or above 1: no steady state exists, so no theoretical values are returned.</summary>
    Overloaded,
    /// <summary>Not enough data for the service-time variance or the arrival rate.</summary>
    InsufficientData
}

/// <summary>Steady-state M/G/1 values in hours and patients. Minutes are derived by the caller for display.</summary>
public sealed record MG1Result(
    TheoreticalStatus Status,
    string? Reason,
    double? Lq,
    double? WqHours,
    double? WsHours,
    double? Ls);

/// <summary>
/// M/G/1 queue (general service, one server) via the Pollaczek-Khinchine formula.
/// Units: lambda in patients per hour, service mean in hours, service variance in hours squared.
/// Assumptions and their limits are documented in docs/ANALYTICS_MODEL.md. The theoretical values are a reference
/// baseline only: scheduled arrivals are not Poisson.
/// </summary>
public static class MG1Model
{
    /// <summary>
    /// Lq = (lambda^2 sigma^2 + rho^2) / (2 (1 - rho)), Wq = Lq / lambda, Ws = Wq + 1/mu, Ls = Lq + rho,
    /// with mu = 1 / meanService and rho = lambda / mu. Requires at least two service observations for the variance.
    /// </summary>
    public static MG1Result Compute(double lambdaPerHour, double meanServiceHours, double serviceVarianceHours2, int serviceSampleCount)
    {
        if (serviceSampleCount < 2)
        {
            return new MG1Result(TheoreticalStatus.InsufficientData,
                $"At least two completed consultations with valid durations are needed for the service-time variance (have {serviceSampleCount}).",
                null, null, null, null);
        }
        if (!(meanServiceHours > 0) || double.IsNaN(serviceVarianceHours2) || serviceVarianceHours2 < 0)
        {
            return new MG1Result(TheoreticalStatus.InsufficientData, "The service-time mean and variance are not valid.", null, null, null, null);
        }
        if (!(lambdaPerHour >= 0))
        {
            return new MG1Result(TheoreticalStatus.InsufficientData, "The arrival rate is not valid.", null, null, null, null);
        }

        var mu = 1.0 / meanServiceHours;
        var rho = lambdaPerHour / mu;
        if (rho >= 1.0)
        {
            return new MG1Result(TheoreticalStatus.Overloaded,
                $"Overloaded: patients arrived at {rho * 100:0}% of what the doctor can see, so the waiting line keeps growing and no model estimate is possible.",
                null, null, null, null);
        }

        if (lambdaPerHour == 0)
        {
            // No arrivals: nobody waits and the only time spent is service.
            return new MG1Result(TheoreticalStatus.Computed, null, 0, 0, meanServiceHours, 0);
        }

        var lq = (lambdaPerHour * lambdaPerHour * serviceVarianceHours2 + rho * rho) / (2.0 * (1.0 - rho));
        var wq = lq / lambdaPerHour;
        var ws = wq + meanServiceHours;
        var ls = lq + rho;
        return new MG1Result(TheoreticalStatus.Computed, null, lq, wq, ws, ls);
    }
}
