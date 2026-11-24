namespace MediCare.Application.Services;

public interface IGracePeriodService
{
    Task<GracePeriodStartResult> StartGracePeriodIfEligibleAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<int> ProcessExpiredGracePeriodsForDoctorAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken);

    /// <summary>
    /// Starts every eligible grace period for the doctor's confirmed, not-checked-in appointments on the current clinic
    /// date whose start has passed, then converts expired grace periods to No-Show. Returns the number of grace periods
    /// started plus the number of No-Shows recorded. Each step keeps its own transaction.
    /// </summary>
    Task<int> ProcessDoctorDayAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken);
}

public sealed record GracePeriodStartResult(
    Guid AppointmentId,
    int FrozenGracePeriodMinutes,
    DateTimeOffset StartedAtUtc,
    bool StartedSuccessfully);
