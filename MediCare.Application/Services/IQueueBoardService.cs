using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public interface IQueueBoardService
{
    Task<IReadOnlyList<DoctorChoice>> GetActiveDoctorsAsync(CancellationToken cancellationToken);
    Task<DoctorQueueBoard> GetDoctorQueueBoardAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    /// <summary>Board for the doctor linked to the signed-in account. Throws UnauthorizedAccessException when no active doctor profile is linked.</summary>
    Task<DoctorQueueBoard> GetOwnDoctorQueueBoardAsync(Guid doctorApplicationUserId, DateOnly date, CancellationToken cancellationToken);
}

public sealed record DoctorChoice(Guid DoctorId, string FullName);

/// <summary>All instants are UTC. Estimates are estimates, not commitments.</summary>
public sealed record DoctorQueueBoard(
    Guid DoctorId,
    string DoctorName,
    DateOnly Date,
    DateTimeOffset GeneratedAtUtc,
    int ExpectedDurationMinutes,
    bool UsesHistoricalAverage,
    BoardActiveEntry? Active,
    IReadOnlyList<BoardScheduledItem> Scheduled,
    IReadOnlyList<BoardWalkInItem> WalkIns);

public sealed record BoardActiveEntry(
    Guid QueueEntryId,
    QueueEntrySource Source,
    QueueEntryStatus Status,
    string PatientName,
    string PatientPhone,
    long? QueueSequence,
    TimeOnly? AppointmentStart,
    DateTimeOffset? CalledAtUtc,
    DateTimeOffset? ConsultationStartedAtUtc,
    Guid PatientId);

public sealed record BoardScheduledItem(
    Guid AppointmentId,
    Guid? QueueEntryId,
    TimeOnly StartTime,
    string PatientName,
    string PatientPhone,
    ScheduledBoardState State,
    DateTimeOffset? GraceEndsAtUtc,
    DateTimeOffset? EstimatedStartUtc,
    bool PreviouslyLeftQueue,
    string? DisruptionWarning = null,
    DateTimeOffset? EstimatedEarliestUtc = null,
    DateTimeOffset? EstimatedLatestUtc = null);

public sealed record BoardWalkInItem(
    Guid QueueEntryId,
    long QueueSequence,
    string PatientName,
    string PatientPhone,
    DateTimeOffset ArrivedAtUtc,
    QueueEntryStatus Status,
    WalkInApprovalState ApprovalState,
    Guid? UsableApprovalId,
    DateTimeOffset? EstimatedStartUtc,
    string? DisruptionWarning = null,
    DateTimeOffset? EstimatedEarliestUtc = null,
    DateTimeOffset? EstimatedLatestUtc = null);

public enum ScheduledBoardState
{
    NotArrived,
    GraceRunning,
    GraceExpired,
    Waiting,
    Called,
    InConsultation,
    Completed,
    NoShow
}

public enum WalkInApprovalState
{
    None,
    Pending,
    Approved,
    Used,
    Rejected,
    Expired
}
