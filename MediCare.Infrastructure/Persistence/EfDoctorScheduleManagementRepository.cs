using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfDoctorScheduleManagementRepository(MediCareDbContext dbContext) : IDoctorScheduleManagementRepository
{
    public Task<Doctor?> GetDoctorRecordAsync(Guid doctorId, CancellationToken cancellationToken) =>
        dbContext.Doctors.Include(doctor => doctor.Specialization)
            .SingleOrDefaultAsync(doctor => doctor.Id == doctorId, cancellationToken);

    public async Task<IReadOnlyList<Doctor>> GetDoctorsWithSpecializationAsync(bool activeOnly, CancellationToken cancellationToken) =>
        await dbContext.Doctors.AsNoTracking()
            .Include(doctor => doctor.Specialization)
            .Where(doctor => !activeOnly || doctor.IsActive)
            .OrderBy(doctor => doctor.FullName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorWorkingHour>> GetAllWorkingHoursAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await dbContext.DoctorWorkingHours
            .Where(item => item.DoctorId == doctorId)
            .ToListAsync(cancellationToken);

    public Task<DoctorWorkingHour?> GetWorkingHourAsync(Guid workingHourId, CancellationToken cancellationToken) =>
        dbContext.DoctorWorkingHours.SingleOrDefaultAsync(item => item.Id == workingHourId, cancellationToken);

    public Task AddWorkingHourAsync(DoctorWorkingHour workingHour, CancellationToken cancellationToken) =>
        dbContext.DoctorWorkingHours.AddAsync(workingHour, cancellationToken).AsTask();

    public Task<DoctorBreak?> GetBreakAsync(Guid breakId, CancellationToken cancellationToken) =>
        dbContext.DoctorBreaks.SingleOrDefaultAsync(item => item.Id == breakId, cancellationToken);

    public async Task<IReadOnlyList<DoctorBreak>> GetBreaksInRangeAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await dbContext.DoctorBreaks
            .Where(item => item.DoctorId == doctorId && item.Date >= fromDate && item.Date <= toDate)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.StartTime)
            .ToListAsync(cancellationToken);

    public Task AddBreakAsync(DoctorBreak doctorBreak, CancellationToken cancellationToken) =>
        dbContext.DoctorBreaks.AddAsync(doctorBreak, cancellationToken).AsTask();

    public void RemoveBreak(DoctorBreak doctorBreak) => dbContext.DoctorBreaks.Remove(doctorBreak);

    public async Task<IReadOnlyList<DoctorHoliday>> GetHolidaysEndingOnOrAfterAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken) =>
        await dbContext.DoctorHolidays
            .Where(item => item.DoctorId == doctorId && item.EndDate >= fromDate)
            .OrderBy(item => item.StartDate)
            .ToListAsync(cancellationToken);

    public Task<DoctorHoliday?> GetHolidayAsync(Guid holidayId, CancellationToken cancellationToken) =>
        dbContext.DoctorHolidays.SingleOrDefaultAsync(item => item.Id == holidayId, cancellationToken);

    public Task AddHolidayAsync(DoctorHoliday holiday, CancellationToken cancellationToken) =>
        dbContext.DoctorHolidays.AddAsync(holiday, cancellationToken).AsTask();

    public void RemoveHoliday(DoctorHoliday holiday) => dbContext.DoctorHolidays.Remove(holiday);

    public async Task<IReadOnlyList<LiveAppointmentFact>> GetLiveAppointmentsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .Where(item => item.DoctorId == doctorId &&
                           item.AppointmentDate >= fromDate &&
                           item.AppointmentDate <= toDate &&
                           (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed))
            .OrderBy(item => item.AppointmentDate)
            .ThenBy(item => item.StartTime)
            .Select(item => new LiveAppointmentFact(item.AppointmentDate, item.StartTime, item.EndTime))
            .ToListAsync(cancellationToken);
}
