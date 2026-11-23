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
}
