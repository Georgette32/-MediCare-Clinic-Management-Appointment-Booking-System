namespace MediCare.Infrastructure.DemoData;

/// <summary>
/// Samplers for the demo history. Every draw goes through the seeded <see cref="Random"/> the seeder passes in, so a seed
/// always produces the same history. They are pure so the statistics can be tested directly.
/// </summary>
internal static class DemoDistributions
{
    /// <summary>Scheduled patients arrive relative to their slot: Normal(-8, 9) minutes, plus a late tail with this chance.</summary>
    public const double LateArrivalChance = 0.12;

    /// <summary>Standard normal draw by the Box-Muller transform.</summary>
    public static double NormalSample(Random rng, double mean, double sd)
    {
        var u1 = 1.0 - rng.NextDouble(); // in (0, 1], so the logarithm is finite
        var u2 = rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + sd * z;
    }

    /// <summary>
    /// Lognormal draw with the given arithmetic mean and coefficient of variation (sd / mean). Right-skewed, like real
    /// consultation lengths: sigma² = ln(1 + CV²) and mu = ln(mean) − sigma² / 2 keep the mean at <paramref name="mean"/>.
    /// </summary>
    public static double LogNormalSample(Random rng, double mean, double cv)
    {
        var sigmaSquared = Math.Log(1.0 + cv * cv);
        var mu = Math.Log(mean) - sigmaSquared / 2.0;
        return Math.Exp(mu + Math.Sqrt(sigmaSquared) * NormalSample(rng, 0.0, 1.0));
    }

    /// <summary>Poisson draw by Knuth's method. Suitable for the small daily rates used here.</summary>
    public static int PoissonSample(Random rng, double lambda)
    {
        var limit = Math.Exp(-lambda);
        var count = 0;
        var product = 1.0;
        do
        {
            count++;
            product *= rng.NextDouble();
        } while (product > limit);
        return count - 1;
    }

    /// <summary>Exponential draw with the given mean.</summary>
    public static double ExponentialSample(Random rng, double mean) => -mean * Math.Log(1.0 - rng.NextDouble());

    /// <summary>
    /// Minutes between a scheduled slot and the patient's arrival (negative = early). Normal(-8, 9); with
    /// <see cref="LateArrivalChance"/> a further 10 to 30 minutes are added. Early arrivals stop at 45 minutes early, and
    /// nobody arrives later than the grace period allows (five minutes of margin before it expires).
    /// </summary>
    public static (double Minutes, bool LateTail) ArrivalOffset(Random rng, int graceMinutes)
    {
        var minutes = NormalSample(rng, -8.0, 9.0);
        var lateTail = rng.NextDouble() < LateArrivalChance;
        if (lateTail)
            minutes += 10.0 + rng.NextDouble() * 20.0;
        minutes = Math.Clamp(minutes, -45.0, Math.Max(-45.0, graceMinutes - 5.0));
        return (minutes, lateTail);
    }

    /// <summary>
    /// Demand for a slot on a past day, before the random draw: base × weekday × position in the window × evening, capped.
    /// The first hour of a window fills fastest, the middle least, and evening windows (starting at 17:00 or later) a little more.
    /// </summary>
    public static double PastDayFill(DayOfWeek day, int windowStart, int windowEnd, int slotStart)
    {
        const double baseFill = 0.72;
        var weekday = day switch
        {
            DayOfWeek.Saturday => 1.15,
            DayOfWeek.Sunday => 1.1,
            DayOfWeek.Monday => 1.0,
            DayOfWeek.Tuesday => 0.95,
            DayOfWeek.Wednesday => 0.95,
            DayOfWeek.Thursday => 0.85,
            _ => 0.7 // Friday: the weekend in Egypt, and no demo window falls on it
        };
        var position = slotStart - windowStart < 60 ? 1.15 : slotStart >= windowEnd - 60 ? 1.0 : 0.9;
        var evening = windowStart >= 17 * 60 ? 1.1 : 1.0;
        return Math.Min(0.97, baseFill * weekday * position * evening);
    }
}
