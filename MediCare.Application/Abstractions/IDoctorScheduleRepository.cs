using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

/// <summary>A half-open clinic-local time interval [Start, End) used for breaks and booked appointments.</summary>
public sealed record ScheduleInterval(TimeOnly Start, TimeOnly End);

/// <summary>Reads and writes working-hour extensions and the dated schedule facts that availability needs.</summary>
public interface IDoctorScheduleRepository
{
    Task<IReadOnlyList<DoctorWorkingHourExtension>> GetApprovedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorWorkingHourExtension>> GetForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken);
    Task<DoctorWorkingHourExtension?> GetExtensionByIdAsync(Guid extensionId, CancellationToken cancellationToken);
    Task<bool> HasLiveExtensionAsync(Guid doctorId, DateOnly date, TimeOnly originalEndTime, CancellationToken cancellationToken);
    Task AddExtensionAsync(DoctorWorkingHourExtension extension, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    /// <summary>Pending and Confirmed appointments on the date, as time intervals.</summary>
    Task<IReadOnlyList<ScheduleInterval>> GetLiveAppointmentIntervalsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    /// <summary>True when a Pending or Confirmed appointment overlaps [start, end) on the date.</summary>
    Task<bool> HasLiveAppointmentOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken);
    Task<IReadOnlyList<MediCare.Domain.Entities.Doctor>> GetActiveDoctorsAsync(CancellationToken cancellationToken);
}
