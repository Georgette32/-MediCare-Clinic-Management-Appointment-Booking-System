using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public interface IWorkingHourExtensionService
{
    /// <summary>Reception/Admin records a Pending extension request for a doctor's window.</summary>
    Task<WorkingHourExtensionResult> RequestAsync(Guid doctorId, DateOnly date, TimeOnly originalEndTime, TimeOnly extendedEndTime, string reason, Guid requestedByUserId, CancellationToken cancellationToken);

    /// <summary>The Doctor records an approved extension of their own schedule.</summary>
    Task<WorkingHourExtensionResult> CreateOwnAsync(Guid doctorApplicationUserId, DateOnly date, TimeOnly originalEndTime, TimeOnly extendedEndTime, string reason, CancellationToken cancellationToken);

    /// <summary>Approve or reject a Pending request. Only the Doctor owning the schedule may decide, and never after expiry.</summary>
    Task<WorkingHourExtensionResult> DecideAsync(Guid extensionId, Guid doctorApplicationUserId, bool approve, string? notes, CancellationToken cancellationToken);

    /// <summary>Cancel a Pending or Approved extension. Reception/Admin may cancel only Pending requests.</summary>
    Task<WorkingHourExtensionResult> CancelAsync(Guid extensionId, Guid actorUserId, bool isDoctorOwner, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkingHourExtensionView>> GetForDoctorAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken);

    /// <summary>The signed-in Doctor's own extensions from the given date onward (Doctor ownership is resolved here).</summary>
    Task<IReadOnlyList<WorkingHourExtensionView>> GetOwnAsync(Guid doctorApplicationUserId, DateOnly fromDate, CancellationToken cancellationToken);

    /// <summary>Actionable (not expired) Pending requests for the Doctor linked to the signed-in account.</summary>
    Task<IReadOnlyList<WorkingHourExtensionView>> GetPendingForDoctorUserAsync(Guid doctorApplicationUserId, CancellationToken cancellationToken);

    /// <summary>The doctor's active window end times for the date, used to populate forms. Empty on holiday dates.</summary>
    Task<IReadOnlyList<TimeOnly>> GetWindowEndTimesAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Window end times for the signed-in Doctor's own schedule on the date.</summary>
    Task<IReadOnlyList<TimeOnly>> GetOwnWindowEndTimesAsync(Guid doctorApplicationUserId, DateOnly date, CancellationToken cancellationToken);

    Task<IReadOnlyList<DoctorOption>> GetActiveDoctorsAsync(CancellationToken cancellationToken);
}

public sealed record WorkingHourExtensionResult(bool Succeeded, string Message, Guid? ExtensionId = null);

public sealed record DoctorOption(Guid DoctorId, string FullName);

public sealed record WorkingHourExtensionView(
    Guid Id,
    Guid DoctorId,
    DateOnly Date,
    TimeOnly OriginalEndTime,
    TimeOnly ExtendedEndTime,
    string Reason,
    WorkingHourExtensionStatus Status,
    bool IsExpired,
    string? DecisionNotes,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc)
{
    /// <summary>Status as shown to users. A past-dated Pending request is shown as Expired.</summary>
    public string DisplayStatus => IsExpired ? "Expired" : Status.ToString();
}
