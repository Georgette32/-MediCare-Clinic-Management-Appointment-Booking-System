using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

/// <summary>A Pending or Confirmed appointment reduced to the facts schedule rules need.</summary>
public sealed record LiveAppointmentFact(DateOnly Date, TimeOnly Start, TimeOnly End);

/// <summary>Reads and writes the doctor schedule aggregate (working windows, dated breaks, holidays, profile).</summary>
public interface IDoctorScheduleManagementRepository
{
    /// <summary>Tracked doctor with its specialization, active or not. Intended for in-transaction writes.</summary>
    Task<Doctor?> GetDoctorRecordAsync(Guid doctorId, CancellationToken cancellationToken);

    /// <summary>Read-only doctors with specializations, ordered by name.</summary>
    Task<IReadOnlyList<Doctor>> GetDoctorsWithSpecializationAsync(bool activeOnly, CancellationToken cancellationToken);

    /// <summary>Tracked working windows for every weekday, including inactive windows.</summary>
    Task<IReadOnlyList<DoctorWorkingHour>> GetAllWorkingHoursAsync(Guid doctorId, CancellationToken cancellationToken);

    Task<DoctorWorkingHour?> GetWorkingHourAsync(Guid workingHourId, CancellationToken cancellationToken);
    Task AddWorkingHourAsync(DoctorWorkingHour workingHour, CancellationToken cancellationToken);

    /// <summary>True when the working window already has at least one history snapshot.</summary>
    Task<bool> HasSnapshotAsync(Guid workingHourId, CancellationToken cancellationToken);
    Task AddSnapshotAsync(DoctorWorkingHourSnapshot snapshot, CancellationToken cancellationToken);

    Task<DoctorBreak?> GetBreakAsync(Guid breakId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorBreak>> GetBreaksInRangeAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
    Task AddBreakAsync(DoctorBreak doctorBreak, CancellationToken cancellationToken);
    void RemoveBreak(DoctorBreak doctorBreak);

    Task<IReadOnlyList<DoctorHoliday>> GetHolidaysEndingOnOrAfterAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken);
    Task<DoctorHoliday?> GetHolidayAsync(Guid holidayId, CancellationToken cancellationToken);
    Task AddHolidayAsync(DoctorHoliday holiday, CancellationToken cancellationToken);
    void RemoveHoliday(DoctorHoliday holiday);

    /// <summary>Pending and Confirmed appointments whose date falls in [fromDate, toDate].</summary>
    Task<IReadOnlyList<LiveAppointmentFact>> GetLiveAppointmentsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
