using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>
/// One in-memory store behind every repository the schedule services read or write. Only the members the schedule
/// tests exercise are implemented; the rest throw so an unexpected call fails loudly.
/// </summary>
internal sealed class ScheduleWorld : IAppointmentRepository, IDoctorScheduleRepository, IDoctorScheduleManagementRepository
{
    public List<Doctor> Doctors { get; } = [];
    public List<DoctorWorkingHour> WorkingHours { get; } = [];
    public List<DoctorBreak> Breaks { get; } = [];
    public List<DoctorHoliday> Holidays { get; } = [];
    public List<DoctorWorkingHourExtension> Extensions { get; } = [];
    public List<ScheduleAppointment> Appointments { get; } = [];
    public List<DoctorWorkingHourSnapshot> Snapshots { get; } = [];

    public sealed record ScheduleAppointment(Guid DoctorId, DateOnly Date, TimeOnly Start, TimeOnly End, AppointmentStatus Status);

    private static bool IsLive(AppointmentStatus status) => status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;

    // ── IAppointmentRepository ─────────────────────────────────────────────

    public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken ct) => Task.FromResult(true);

    public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken ct) =>
        Task.FromResult(false);

    public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken ct) =>
        Task.FromResult(Doctors.FirstOrDefault(doctor => doctor.Id == doctorId && doctor.IsActive));

    public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken ct) =>
        Task.FromResult(Doctors.FirstOrDefault(doctor => doctor.ApplicationUserId == applicationUserId && doctor.IsActive));

    public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHour>>(WorkingHours
            .Where(item => item.DoctorId == doctorId && item.DayOfWeek == dayOfWeek && item.IsActive)
            .OrderBy(item => item.StartTime)
            .ToArray());

    public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken ct) =>
        Task.FromResult(Holidays.Any(item => item.DoctorId == doctorId && item.StartDate <= date && item.EndDate >= date));

    public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct) =>
        Task.FromResult(Breaks.Any(item => item.DoctorId == doctorId && item.Date == date && item.StartTime < end && item.EndTime > start));

    public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken ct) =>
        Task.FromResult(Appointments.Any(item => item.DoctorId == doctorId && item.Date == date && item.Start < end && item.End > start && IsLive(item.Status)));

    public Task<IReadOnlyList<Appointment>> GetLiveForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken ct) => Task.FromResult<IReadOnlyList<Appointment>>([]);
    public Task<bool> HasLivePatientOverlapAsync(Guid patientId, DateOnly date, TimeOnly start, TimeOnly end, IReadOnlyCollection<Guid> excludedAppointmentIds, CancellationToken ct) => Task.FromResult(false);
    public Task<Appointment?> GetLiveReplacementAsync(Guid originalAppointmentId, CancellationToken ct) => Task.FromResult<Appointment?>(null);
    public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken ct) => Task.FromResult(false);

    public Task AddAsync(MediCare.Domain.Entities.Appointment appointment, CancellationToken ct) => throw new NotSupportedException();
    public Task<MediCare.Domain.Entities.Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<MediCare.Domain.Entities.Appointment>> GetPendingAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<MediCare.Domain.Entities.Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) => throw new NotSupportedException();

    // ── IDoctorScheduleRepository ──────────────────────────────────────────

    public Task<IReadOnlyList<DoctorWorkingHourExtension>> GetApprovedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHourExtension>>(Extensions
            .Where(item => item.DoctorId == doctorId && item.Date == date && item.Status == WorkingHourExtensionStatus.Approved)
            .ToArray());

    public Task<IReadOnlyList<DoctorWorkingHourExtension>> GetForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHourExtension>>(Extensions
            .Where(item => item.DoctorId == doctorId && item.Date >= fromDate)
            .OrderBy(item => item.Date)
            .ToArray());

    public Task<DoctorWorkingHourExtension?> GetExtensionByIdAsync(Guid extensionId, CancellationToken ct) =>
        Task.FromResult(Extensions.FirstOrDefault(item => item.Id == extensionId));

    public Task<bool> HasLiveExtensionAsync(Guid doctorId, DateOnly date, TimeOnly originalEndTime, CancellationToken ct) => Task.FromResult(false);
    public Task AddExtensionAsync(DoctorWorkingHourExtension extension, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorBreak>>(Breaks.Where(item => item.DoctorId == doctorId && item.Date == date).ToArray());

    public Task<IReadOnlyList<ScheduleInterval>> GetLiveAppointmentIntervalsAsync(Guid doctorId, DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScheduleInterval>>(Appointments
            .Where(item => item.DoctorId == doctorId && item.Date == date && IsLive(item.Status))
            .Select(item => new ScheduleInterval(item.Start, item.End))
            .ToArray());

    public Task<bool> HasLiveAppointmentOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken ct) =>
        Task.FromResult(Appointments.Any(item => item.DoctorId == doctorId && item.Date == date && item.Start < end && item.End > start && IsLive(item.Status)));

    public Task<IReadOnlyList<Doctor>> GetActiveDoctorsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Doctor>>(Doctors.Where(doctor => doctor.IsActive).ToArray());

    // ── IDoctorScheduleManagementRepository ────────────────────────────────

    public Task<Doctor?> GetDoctorRecordAsync(Guid doctorId, CancellationToken ct) =>
        Task.FromResult(Doctors.FirstOrDefault(doctor => doctor.Id == doctorId));

    public Task<IReadOnlyList<Doctor>> GetDoctorsWithSpecializationAsync(bool activeOnly, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Doctor>>(Doctors
            .Where(doctor => !activeOnly || doctor.IsActive)
            .OrderBy(doctor => doctor.FullName)
            .ToArray());

    public Task<IReadOnlyList<DoctorWorkingHour>> GetAllWorkingHoursAsync(Guid doctorId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHour>>(WorkingHours.Where(item => item.DoctorId == doctorId).ToArray());

    public Task<DoctorWorkingHour?> GetWorkingHourAsync(Guid workingHourId, CancellationToken ct) =>
        Task.FromResult(WorkingHours.FirstOrDefault(item => item.Id == workingHourId));

    public Task AddWorkingHourAsync(DoctorWorkingHour workingHour, CancellationToken ct)
    {
        WorkingHours.Add(workingHour);
        return Task.CompletedTask;
    }

    public Task<bool> HasSnapshotAsync(Guid workingHourId, CancellationToken ct) =>
        Task.FromResult(Snapshots.Any(item => item.WorkingHourId == workingHourId));

    public Task AddSnapshotAsync(DoctorWorkingHourSnapshot snapshot, CancellationToken ct)
    {
        Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<DoctorBreak?> GetBreakAsync(Guid breakId, CancellationToken ct) =>
        Task.FromResult(Breaks.FirstOrDefault(item => item.Id == breakId));

    public Task<IReadOnlyList<DoctorBreak>> GetBreaksInRangeAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorBreak>>(Breaks
            .Where(item => item.DoctorId == doctorId && item.Date >= fromDate && item.Date <= toDate)
            .ToArray());

    public Task AddBreakAsync(DoctorBreak doctorBreak, CancellationToken ct)
    {
        Breaks.Add(doctorBreak);
        return Task.CompletedTask;
    }

    public void RemoveBreak(DoctorBreak doctorBreak) => Breaks.Remove(doctorBreak);

    public Task<IReadOnlyList<DoctorHoliday>> GetHolidaysEndingOnOrAfterAsync(Guid doctorId, DateOnly fromDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorHoliday>>(Holidays
            .Where(item => item.DoctorId == doctorId && item.EndDate >= fromDate)
            .ToArray());

    public Task<DoctorHoliday?> GetHolidayAsync(Guid holidayId, CancellationToken ct) =>
        Task.FromResult(Holidays.FirstOrDefault(item => item.Id == holidayId));

    public Task AddHolidayAsync(DoctorHoliday holiday, CancellationToken ct)
    {
        Holidays.Add(holiday);
        return Task.CompletedTask;
    }

    public void RemoveHoliday(DoctorHoliday holiday) => Holidays.Remove(holiday);

    public Task<IReadOnlyList<LiveAppointmentFact>> GetLiveAppointmentsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LiveAppointmentFact>>(Appointments
            .Where(item => item.DoctorId == doctorId && item.Date >= fromDate && item.Date <= toDate && IsLive(item.Status))
            .OrderBy(item => item.Date).ThenBy(item => item.Start)
            .Select(item => new LiveAppointmentFact(item.Date, item.Start, item.End))
            .ToArray());
}
