using MediCare.Domain.Enums;

namespace MediCare.Application.Abstractions;

/// <summary>Flat, read-only appointment row with the names and links the booking screens need.</summary>
public sealed record AppointmentRecord(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string PatientPhone,
    Guid DoctorId,
    string DoctorName,
    string Specialization,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    AppointmentStatus Status,
    string? CancellationReason,
    Guid? RescheduledFromAppointmentId,
    DateOnly? OriginalDate,
    TimeOnly? OriginalStartTime);

/// <summary>A live (Pending or Confirmed) replacement that points at an original appointment.</summary>
public sealed record LiveReplacementRecord(Guid OriginalAppointmentId, Guid ReplacementId, DateOnly Date, TimeOnly StartTime, AppointmentStatus Status);

public sealed record PatientSearchRecord(Guid PatientId, Guid ApplicationUserId, string FullName, string PhoneNumber, string? Email);

/// <summary>Read-only queries for appointment and patient screens. Implementations must not track entities.</summary>
public interface IAppointmentQueryRepository
{
    Task<Guid?> GetPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken);
    Task<PatientSearchRecord?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientSearchRecord>> SearchPatientsAsync(string? exactPhone, string? phoneDigits, string? nameContains, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppointmentRecord>> GetForPatientAsync(Guid patientId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppointmentRecord>> GetForDateAsync(DateOnly date, Guid? doctorId, CancellationToken cancellationToken);
    Task<AppointmentRecord?> GetRecordAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LiveReplacementRecord>> GetLiveReplacementsAsync(Guid[] originalAppointmentIds, CancellationToken cancellationToken);
}
