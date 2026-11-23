using MediCare.Application.DTOs.Analytics;
using MediCare.Application.DTOs.Scheduling;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Interfaces.Persistence;

public interface IAppointmentRepository
{
    Task<int> CountAsync(
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<int> CountUpcomingAsync(
        CancellationToken cancellationToken = default);

    Task<int> CountByDateRangeAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeakHoursDto>> GetPeakHoursAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpecializationAnalyticsDto>> GetPopularSpecializationsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);

    // --- Scheduling module ---

    Task<bool> PatientExistsAsync(Guid patientProfileId, CancellationToken cancellationToken);

    Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly date,
        CancellationToken cancellationToken);

    Task<DoctorProfile?> GetDoctorProfileAsync(Guid doctorProfileId, CancellationToken cancellationToken);

    Task<DoctorProfile?> GetDoctorProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(
        Guid doctorProfileId,
        DayOfWeek dayOfWeek,
        CancellationToken cancellationToken);

    Task<bool> IsHolidayAsync(Guid doctorProfileId, DateOnly date, CancellationToken cancellationToken);

    Task<bool> HasBreakOverlapAsync(
        Guid doctorProfileId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        CancellationToken cancellationToken);

    Task<bool> HasAppointmentConflictAsync(
        Guid doctorProfileId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        Guid? exceptAppointmentId,
        CancellationToken cancellationToken);

    Task AddAsync(Appointment appointment, CancellationToken cancellationToken);

    Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken);

    Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly date,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAsync(
        Guid doctorProfileId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingAppointmentDto>> GetPendingWithNamesAsync(CancellationToken cancellationToken);

    Task<string?> GetPatientNameAsync(Guid patientProfileId, CancellationToken cancellationToken);
}
