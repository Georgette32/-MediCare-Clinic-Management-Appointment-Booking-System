using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Doctor-leaves-early disruptions. Reception and Admin only (enforced by the web layer). Every mutating operation runs
/// in its own serializable transaction; patient notifications are published after the commit.
/// </summary>
public interface IDisruptionService
{
    /// <summary>Blocks the doctor's remaining time on the date and compiles the worklist. Does not change working hours.</summary>
    Task<Guid> RecordAsync(Guid doctorId, DateOnly date, TimeOnly fromTime, string reason, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DisruptionListItem>> GetRecentAsync(int take, CancellationToken cancellationToken);
    Task<DisruptionWorklist> GetWorklistAsync(Guid disruptionId, CancellationToken cancellationToken);
    /// <summary>Records that staff reached the patient. The item stays open.</summary>
    Task LogContactAsync(Guid itemId, Guid actorUserId, string? notes, CancellationToken cancellationToken);
    /// <summary>Reschedules the appointment through the clinic rules, then closes the item as Rescheduled.</summary>
    Task<Guid> RescheduleFromWorklistAsync(Guid itemId, Guid actorUserId, Guid newDoctorId, DateOnly newDate, TimeOnly newStartTime, string? notes, CancellationToken cancellationToken);
    /// <summary>Cancels the appointment (or the waiting walk-in entry) with reason clinic unavailable, then closes the item as Cancelled.</summary>
    Task CancelFromWorklistAsync(Guid itemId, Guid actorUserId, string? notes, CancellationToken cancellationToken);
    /// <summary>Records that the doctor's day ended with unresolved patients: cancels every unresolved item and notifies patients.</summary>
    Task RecordDayEndAsync(Guid disruptionId, Guid actorUserId, CancellationToken cancellationToken);
    /// <summary>Cancels unresolved appointments whose start has passed. Idempotent. Returns how many were cancelled.</summary>
    Task<int> ProcessUnresolvedAsync(DateTimeOffset clinicNowUtc, CancellationToken cancellationToken);
    /// <summary>
    /// Previous-day path of the clinic-unavailable processor: cancels the appointment with reason clinic unavailable when
    /// it is still Confirmed, on an open worklist item, and not being served. Returns false when nothing was changed.
    /// </summary>
    Task<bool> CancelPreviousDayWorklistAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);
}

public sealed record DisruptionListItem(
    Guid Id,
    string DoctorName,
    DateOnly Date,
    TimeOnly FromTime,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    bool DayEndRecorded);

public sealed record DisruptionWorklist(
    Guid Id,
    string DoctorName,
    DateOnly Date,
    TimeOnly FromTime,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    bool DayEndRecorded,
    DateTimeOffset? DayEndRecordedAtUtc,
    IReadOnlyList<WorklistItemView> Items);

public sealed record WorklistItemView(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string PatientPhone,
    Guid? AppointmentId,
    DateOnly? AppointmentDate,
    TimeOnly? AppointmentStart,
    AppointmentStatus? AppointmentStatus,
    Guid? QueueEntryId,
    QueueEntryStatus? QueueStatus,
    DisruptionOutcome Outcome,
    bool IsOpen,
    DateTimeOffset? LastContactedAtUtc,
    string? LastContactedByName,
    string? Notes,
    IReadOnlyList<ContactAttemptView> History)
{
    /// <summary>Only a live appointment or a waiting entry can still be contacted, rescheduled or cancelled.</summary>
    public bool CanAct =>
        IsOpen &&
        (AppointmentStatus is null || AppointmentStatus == MediCare.Domain.Enums.AppointmentStatus.Confirmed) &&
        (QueueStatus is null || QueueStatus == QueueEntryStatus.Waiting);

    /// <summary>Walk-ins have no appointment to move, so only scheduled items can be rescheduled.</summary>
    public bool CanReschedule => CanAct && AppointmentId is not null;
}

public sealed record ContactAttemptView(
    DateTimeOffset ContactedAtUtc,
    string ContactedByName,
    string? Notes,
    DisruptionOutcome Outcome);

/// <summary>Staff-facing warning text. Clinic-local times are formatted by the caller.</summary>
public static class DisruptionWarnings
{
    public static string ForAppointment(string doctorName, TimeOnly fromTime, string reason) =>
        $"{doctorName} left early from {fromTime:HH\\:mm} ({reason}). This appointment is on the disruption worklist.";

    public static string ForQueueEntry(string doctorName, TimeOnly fromTime, string reason) =>
        $"{doctorName} left early from {fromTime:HH\\:mm} ({reason}). This patient is on the disruption worklist.";
}
