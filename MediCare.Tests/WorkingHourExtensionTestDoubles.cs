using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>
/// In-memory schedule store for extensions, breaks and booked intervals. Tests add booked intervals directly to
/// simulate Pending/Confirmed appointments that the availability and cancellation rules must respect.
/// </summary>
internal sealed class InMemoryDoctorScheduleRepository : IDoctorScheduleRepository
{
    public Dictionary<Guid, DoctorWorkingHourExtension> Extensions { get; } = [];
    public List<DoctorBreak> Breaks { get; } = [];
    public List<(Guid DoctorId, DateOnly Date, TimeOnly Start, TimeOnly End)> Booked { get; } = [];

    public Task<IReadOnlyList<DoctorWorkingHourExtension>> GetApprovedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHourExtension>>(Extensions.Values
            .Where(item => item.DoctorId == doctorId && item.Date == date && item.Status == WorkingHourExtensionStatus.Approved)
            .ToArray());

    public Task<IReadOnlyList<DoctorWorkingHourExtension>> GetForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHourExtension>>(Extensions.Values
            .Where(item => item.DoctorId == doctorId && item.Date >= fromDate)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.OriginalEndTime)
            .ToArray());

    public Task<DoctorWorkingHourExtension?> GetExtensionByIdAsync(Guid extensionId, CancellationToken cancellationToken) =>
        Task.FromResult(Extensions.GetValueOrDefault(extensionId));

    public Task<bool> HasLiveExtensionAsync(Guid doctorId, DateOnly date, TimeOnly originalEndTime, CancellationToken cancellationToken) =>
        Task.FromResult(Extensions.Values.Any(item =>
            item.DoctorId == doctorId && item.Date == date && item.OriginalEndTime == originalEndTime &&
            item.Status is WorkingHourExtensionStatus.Pending or WorkingHourExtensionStatus.Approved));

    public Task AddExtensionAsync(DoctorWorkingHourExtension extension, CancellationToken cancellationToken)
    {
        Extensions[extension.Id] = extension;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorBreak>>(Breaks.Where(item => item.DoctorId == doctorId && item.Date == date).ToArray());

    public Task<IReadOnlyList<ScheduleInterval>> GetLiveAppointmentIntervalsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScheduleInterval>>(Booked
            .Where(item => item.DoctorId == doctorId && item.Date == date)
            .Select(item => new ScheduleInterval(item.Start, item.End))
            .ToArray());

    public Task<bool> HasLiveAppointmentOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) =>
        Task.FromResult(Booked.Any(item => item.DoctorId == doctorId && item.Date == date && item.Start < end && item.End > start));

    public Task<IReadOnlyList<Doctor>> GetActiveDoctorsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Doctor>>([]);
}

internal static class ScheduleTestData
{
    /// <summary>A doctor with a 20-minute appointment duration.</summary>
    public static Doctor NewDoctor(Guid? userId = null) =>
        new(Guid.NewGuid(), userId ?? Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 20);

    public static void AddWeeklyWindow(InMemoryAppointmentRepository appointments, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end) =>
        appointments.WorkingHours.Add(DoctorWorkingHour.Create(doctorId, date.DayOfWeek, start, end));

    public static DoctorWorkingHourExtension ApprovedExtension(Guid doctorId, DateOnly date, TimeOnly originalEnd, TimeOnly extendedEnd, DateTimeOffset now) =>
        DoctorWorkingHourExtension.CreateApprovedByDoctor(doctorId, date, originalEnd, extendedEnd, "Clinic overflow", Guid.NewGuid(), now);

    public static DoctorWorkingHourExtension PendingExtension(Guid doctorId, DateOnly date, TimeOnly originalEnd, TimeOnly extendedEnd, DateTimeOffset now) =>
        DoctorWorkingHourExtension.Request(doctorId, date, originalEnd, extendedEnd, "Clinic overflow", Guid.NewGuid(), now);
}
