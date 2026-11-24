using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfDoctorScheduleRepository(MediCareDbContext dbContext) : IDoctorScheduleRepository
{
    public async Task<IReadOnlyList<DoctorWorkingHourExtension>> GetApprovedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.DoctorWorkingHourExtensions
            .Where(item => item.DoctorId == doctorId && item.Date == date && item.Status == WorkingHourExtensionStatus.Approved)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorWorkingHourExtension>> GetForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken) =>
        await dbContext.DoctorWorkingHourExtensions
            .Where(item => item.DoctorId == doctorId && item.Date >= fromDate)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.OriginalEndTime)
            .ToListAsync(cancellationToken);

    public Task<DoctorWorkingHourExtension?> GetExtensionByIdAsync(Guid extensionId, CancellationToken cancellationToken) =>
        dbContext.DoctorWorkingHourExtensions.SingleOrDefaultAsync(item => item.Id == extensionId, cancellationToken);

    public Task<bool> HasLiveExtensionAsync(Guid doctorId, DateOnly date, TimeOnly originalEndTime, CancellationToken cancellationToken) =>
        dbContext.DoctorWorkingHourExtensions.AnyAsync(item =>
            item.DoctorId == doctorId &&
            item.Date == date &&
            item.OriginalEndTime == originalEndTime &&
            (item.Status == WorkingHourExtensionStatus.Pending || item.Status == WorkingHourExtensionStatus.Approved),
            cancellationToken);

    public Task AddExtensionAsync(DoctorWorkingHourExtension extension, CancellationToken cancellationToken) =>
        dbContext.DoctorWorkingHourExtensions.AddAsync(extension, cancellationToken).AsTask();

    public async Task<IReadOnlyList<DoctorBreak>> GetBreaksAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.DoctorBreaks
            .Where(item => item.DoctorId == doctorId && item.Date == date)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScheduleInterval>> GetLiveAppointmentIntervalsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .Where(item => item.DoctorId == doctorId &&
                           item.AppointmentDate == date &&
                           (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed))
            .Select(item => new ScheduleInterval(item.StartTime, item.EndTime))
            .ToListAsync(cancellationToken);

    public Task<bool> HasLiveAppointmentOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) =>
        dbContext.Appointments.AnyAsync(item =>
            item.DoctorId == doctorId &&
            item.AppointmentDate == date &&
            item.StartTime < end &&
            item.EndTime > start &&
            (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed),
            cancellationToken);

    public async Task<IReadOnlyList<Doctor>> GetActiveDoctorsAsync(CancellationToken cancellationToken) =>
        await dbContext.Doctors
            .Where(doctor => doctor.IsActive)
            .ToListAsync(cancellationToken);
}
