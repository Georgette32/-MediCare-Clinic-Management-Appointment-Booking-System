namespace MediCare.Application.Services;

public interface IWalkInAdmissionApprovalService
{
    Task<Guid> RequestAsync(Guid queueEntryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PendingWalkInAdmissionRequest>> GetPendingForDoctorAsync(Guid doctorApplicationUserId, CancellationToken cancellationToken);
    Task<string> DecideAsync(Guid approvalId, Guid doctorApplicationUserId, bool approve, string? notes, CancellationToken cancellationToken);
    Task<WalkInCallResult> CallAsync(Guid queueEntryId, Guid approvalId, CancellationToken cancellationToken);
}

public sealed record PendingWalkInAdmissionRequest(
    Guid ApprovalId,
    Guid QueueEntryId,
    string PatientName,
    long QueueSequence,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record WalkInCallResult(bool Called, string Message);
