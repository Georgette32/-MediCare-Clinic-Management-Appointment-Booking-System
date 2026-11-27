using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Abstractions;

/// <summary>Doctor identity and service parameters needed by the analytics engine.</summary>
public sealed record AnalyticsDoctor(Guid DoctorId, Guid ApplicationUserId, string FullName, int AppointmentDurationMinutes, bool IsActive);

/// <summary>
/// One attendance attempt. <see cref="ClinicUnavailable"/> is true when the attempt is tied to an appointment cancelled
/// with reason "clinic unavailable"; such attempts are excluded from every queueing measure. The appointment date and
/// start time (clinic-local) are set only for attempts tied to an appointment.
/// </summary>
public sealed record AnalyticsVisitRow(
    Guid DoctorId,
    Guid PatientId,
    DateOnly QueueDate,
    QueueEntrySource Source,
    QueueEntryStatus Status,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? CalledAt,
    DateTimeOffset? ConsultationStartedAt,
    DateTimeOffset? CompletedAt,
    bool ClinicUnavailable,
    DateOnly? AppointmentDate = null,
    TimeOnly? AppointmentStartTime = null,
    DateTimeOffset? ClosedAtUtc = null);

/// <summary>One appointment for the no-show rate and the scheduled-start profile.</summary>
public sealed record AnalyticsAppointmentRow(
    Guid DoctorId,
    DateOnly AppointmentDate,
    TimeOnly StartTime,
    AppointmentStatus Status,
    bool ClinicUnavailable);

/// <summary>
/// An active weekly working window (clinic-local times). <see cref="WorkingHourId"/> links it to its snapshots; null in
/// tests that do not model history.
/// </summary>
public sealed record AnalyticsWorkingHourRow(Guid DoctorId, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, Guid? WorkingHourId = null);

/// <summary>
/// One recorded state of a working window, in force from <see cref="EffectiveFrom"/> (clinic-local; DateOnly.MinValue is
/// the baseline before history was recorded). Several rows can share a date: the latest <see cref="RecordedAtUtc"/> wins.
/// </summary>
public sealed record AnalyticsWorkingHourSnapshotRow(
    Guid WorkingHourId,
    Guid DoctorId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateTimeOffset RecordedAtUtc);

public sealed record AnalyticsHolidayRow(Guid DoctorId, DateOnly StartDate, DateOnly EndDate);

/// <summary>An Approved working-hour extension: the window that ended at OriginalEndTime now ends at ExtendedEndTime.</summary>
public sealed record AnalyticsExtensionRow(Guid DoctorId, DateOnly Date, TimeOnly OriginalEndTime, TimeOnly ExtendedEndTime);

public sealed record AnalyticsBreakRow(Guid DoctorId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

/// <summary>A completed consultation's start and end, for the consultation length history used by the live board.</summary>
public sealed record AnalyticsServiceHistoryRow(Guid DoctorId, DateOnly QueueDate, DateTimeOffset StartedAt, DateTimeOffset CompletedAt);

/// <summary>
/// Read-only access for analytics. Every method is one query (AsNoTracking projection). A null doctor-id set means all doctors.
/// </summary>
public interface IAnalyticsRepository
{
    Task<IReadOnlyList<AnalyticsDoctor>> GetDoctorsAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken);
    Task<Guid?> GetDoctorIdForApplicationUserAsync(Guid applicationUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnalyticsVisitRow>> GetVisitsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnalyticsAppointmentRow>> GetAppointmentsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnalyticsWorkingHourRow>> GetActiveWorkingHoursAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnalyticsHolidayRow>> GetHolidaysAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnalyticsExtensionRow>> GetApprovedExtensionsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnalyticsBreakRow>> GetBreaksAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>Every working-window snapshot in force on or before <paramref name="toDate"/>, active or not.</summary>
    Task<IReadOnlyList<AnalyticsWorkingHourSnapshotRow>> GetWorkingHourSnapshotsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>Completed consultations with both timestamps, excluding "clinic unavailable" attempts.</summary>
    Task<IReadOnlyList<AnalyticsServiceHistoryRow>> GetServiceHistoryAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
