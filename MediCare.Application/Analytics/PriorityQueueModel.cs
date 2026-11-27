namespace MediCare.Application.Analytics;

/// <summary>
/// Non-preemptive priority M/G/1 queue (Cobham's formula) for a clinic where booked patients are called before walk-ins.
/// Two classes share one server (the doctor): class 1 is booked patients (priority), class 2 is walk-ins.
/// Assumptions: Poisson arrivals in each class, both classes drawn from the same service-time distribution, and a
/// consultation in progress is never interrupted. With W0 = lambda E[S²] / 2 the mean work left by whoever is in
/// service, Wq1 = W0 / (1 - rho1) and Wq2 = W0 / ((1 - rho1)(1 - rho)), where rho1 and rho are the booked and total
/// utilisation. The arrival-weighted average of the two waits equals the FCFS Pollaczek-Khinchine wait (conservation
/// law), so the priority rule moves waiting from booked patients to walk-ins without changing the total.
/// Units: rates per hour, times in hours, variance in hours squared. Like <see cref="MG1Model"/>, this is a reference
/// baseline only: scheduled arrivals are not Poisson, so the figures are an approximation of the real clinic.
/// </summary>
public static class PriorityQueueModel
{
    /// <summary>
    /// Mean waits in hours for booked patients and walk-ins, or null when an input is invalid, when there are no
    /// arrivals, or when the total utilisation is at or above 1 (no steady state).
    /// </summary>
    public static (double WqScheduledHours, double WqWalkInHours)? Compute(
        double scheduledPerHour,
        double walkInPerHour,
        double meanServiceHours,
        double serviceVarianceHours2)
    {
        if (!(scheduledPerHour >= 0) || !(walkInPerHour >= 0) || !(meanServiceHours > 0) || !(serviceVarianceHours2 >= 0))
            return null;

        var lambda = scheduledPerHour + walkInPerHour;
        if (lambda == 0)
            return null;

        var rhoScheduled = scheduledPerHour * meanServiceHours;
        var rho = lambda * meanServiceHours;
        if (rho >= 1.0)
            return null;

        var secondMoment = serviceVarianceHours2 + meanServiceHours * meanServiceHours;
        var w0 = lambda * secondMoment / 2.0;
        var wqScheduled = w0 / (1.0 - rhoScheduled);
        var wqWalkIn = w0 / ((1.0 - rhoScheduled) * (1.0 - rho));
        return (wqScheduled, wqWalkIn);
    }
}
