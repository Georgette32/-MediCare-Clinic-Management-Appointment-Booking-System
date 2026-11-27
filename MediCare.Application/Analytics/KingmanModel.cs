namespace MediCare.Application.Analytics;

/// <summary>
/// G/G/1 approximation (Kingman): Wq ≈ (rho / (1 - rho)) * ((Ca² + Cs²) / 2) * E[S], Lq = lambda * Wq.
/// Ca² is the squared coefficient of variation of the time between arrivals (1 for random Poisson arrivals, 0 for
/// perfectly even ones) and Cs² that of the consultation length. With Ca² = 1 it equals Pollaczek-Khinchine exactly,
/// so it generalises the M/G/1 baseline to booked arrivals instead of replacing it. Units as in <see cref="MG1Model"/>.
/// </summary>
public static class KingmanModel
{
    /// <summary>Null when the inputs are not valid or rho is at or above 1 (no steady state).</summary>
    public static (double WqHours, double Lq)? Compute(double lambdaPerHour, double meanServiceHours, double serviceVarianceHours2, double arrivalCa2)
    {
        if (!(lambdaPerHour > 0) || !(meanServiceHours > 0) || !(serviceVarianceHours2 >= 0) || !(arrivalCa2 >= 0))
            return null;

        var rho = lambdaPerHour * meanServiceHours;
        if (rho >= 1.0)
            return null;

        var serviceCs2 = serviceVarianceHours2 / (meanServiceHours * meanServiceHours);
        var wq = rho / (1.0 - rho) * (arrivalCa2 + serviceCs2) / 2.0 * meanServiceHours;
        return (wq, lambdaPerHour * wq);
    }
}
