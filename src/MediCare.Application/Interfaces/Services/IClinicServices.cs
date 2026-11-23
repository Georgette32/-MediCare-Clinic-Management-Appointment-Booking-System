namespace MediCare.Application.Interfaces.Services;

public interface ICheckInService
{
    Task<ScheduledCheckInResult> CheckInScheduledPatientAsync(
        Guid appointmentId,
        Guid expectedDoctorProfileId,
        CancellationToken cancellationToken);

    Task<Guid> RegisterWalkInAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        CancellationToken cancellationToken);
}

public sealed record ScheduledCheckInResult(
    Guid? QueueEntryId,
    bool RequiresWalkInRegistration,
    string? Message);

public interface IExpectedDurationService
{
    Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(
        Guid doctorProfileId,
        DateOnly clinicDate,
        CancellationToken cancellationToken);
}

public sealed record ExpectedDurationResult(
    int DurationMinutes,
    bool UsedHistoricalAverage,
    int ValidSampleCount,
    double? RawAverageMinutes);

public interface IGracePeriodService
{
    Task<GracePeriodStartResult> StartGracePeriodIfEligibleAsync(
        Guid appointmentId,
        CancellationToken cancellationToken);

    Task<int> ProcessExpiredGracePeriodsForDoctorAsync(
        Guid doctorProfileId,
        DateOnly clinicDate,
        CancellationToken cancellationToken);
}

public sealed record GracePeriodStartResult(
    Guid AppointmentId,
    int FrozenGracePeriodMinutes,
    DateTime StartedAtUtc,
    bool StartedSuccessfully);

public interface IWalkInAdmissionApprovalService
{
    Task<Guid> RequestAsync(Guid queueEntryId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingWalkInAdmissionRequest>> GetPendingForDoctorAsync(
        Guid doctorUserId,
        CancellationToken cancellationToken);

    Task<string> DecideAsync(
        Guid approvalId,
        Guid doctorUserId,
        bool approve,
        string? notes,
        CancellationToken cancellationToken);

    Task<WalkInCallResult> CallAsync(
        Guid queueEntryId,
        Guid approvalId,
        CancellationToken cancellationToken);
}

public sealed record PendingWalkInAdmissionRequest(
    Guid ApprovalId,
    Guid QueueEntryId,
    string PatientName,
    long QueueSequence,
    DateTime ArrivedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record WalkInCallResult(bool Called, string Message);
