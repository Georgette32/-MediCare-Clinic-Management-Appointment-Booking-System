namespace MediCare.Application.Services;

public interface IGracePeriodService
{
    Task<GracePeriodStartResult> StartGracePeriodIfEligibleAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<int> ProcessExpiredGracePeriodsForDoctorAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken);
}

public sealed record GracePeriodStartResult(
    Guid AppointmentId,
    int FrozenGracePeriodMinutes,
    DateTimeOffset StartedAtUtc,
    bool StartedSuccessfully);
