using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IAppointmentRepository
{
    Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken);
    Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken);
    Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken cancellationToken);
    Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken);
    Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken cancellationToken);
    Task AddAsync(Appointment appointment, CancellationToken cancellationToken);
    Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// True when a Pending or Confirmed appointment of the patient, with any doctor, overlaps [start, end) on the date.
    /// The excluded appointments (the appointment itself and, for a replacement, its linked original) are ignored.
    /// </summary>
    Task<bool> HasLivePatientOverlapAsync(Guid patientId, DateOnly date, TimeOnly start, TimeOnly end, IReadOnlyCollection<Guid> excludedAppointmentIds, CancellationToken cancellationToken);

    /// <summary>Pending and Confirmed appointments of the doctor on or after the date, ordered by date and start.</summary>
    Task<IReadOnlyList<Appointment>> GetLiveForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken);

    /// <summary>The live (Pending or Confirmed) replacement that points at the original, or null.</summary>
    Task<Appointment?> GetLiveReplacementAsync(Guid originalAppointmentId, CancellationToken cancellationToken);
}
