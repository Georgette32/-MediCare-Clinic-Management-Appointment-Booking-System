using MediCare.Application.DTOs.Analytics;
using MediCare.Application.DTOs.Scheduling;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AppointmentRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> CountAsync(
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default) =>
        status.HasValue
            ? _dbContext.Appointments.CountAsync(
                a => a.Status == status.Value, cancellationToken)
            : _dbContext.Appointments.CountAsync(cancellationToken);

    public Task<int> CountUpcomingAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return _dbContext.Appointments.CountAsync(
            a => a.ScheduledAtUtc >= now &&
                 a.Status != AppointmentStatus.Cancelled &&
                 a.Status != AppointmentStatus.Completed &&
                 a.Status != AppointmentStatus.NoShow &&
                 a.Status != AppointmentStatus.Rejected &&
                 a.Status != AppointmentStatus.Rescheduled,
            cancellationToken);
    }

    public async Task<int> CountByDateRangeAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Appointments.Where(
            a => a.ScheduledAtUtc >= startDateUtc &&
                 a.ScheduledAtUtc < endDateUtc);

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PeakHoursDto>> GetPeakHoursAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Appointments
            .Where(a => a.ScheduledAtUtc >= startDateUtc &&
                        a.ScheduledAtUtc < endDateUtc)
            .GroupBy(a => a.ScheduledAtUtc.Hour)
            .Select(g => new PeakHoursDto
            {
                Hour = g.Key,
                AppointmentCount = g.Count()
            })
            .OrderByDescending(x => x.AppointmentCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpecializationAnalyticsDto>> GetPopularSpecializationsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        return await (
            from appointment in _dbContext.Appointments
            join doctor in _dbContext.DoctorProfiles
                on appointment.DoctorProfileId equals doctor.Id
            join specialization in _dbContext.Specializations
                on doctor.SpecializationId equals specialization.Id
            where appointment.ScheduledAtUtc >= startDateUtc &&
                  appointment.ScheduledAtUtc < endDateUtc
            group appointment by new
            {
                specialization.Id,
                specialization.Name
            }
            into specializationGroup
            select new SpecializationAnalyticsDto
            {
                SpecializationId = specializationGroup.Key.Id,
                SpecializationName = specializationGroup.Key.Name,
                AppointmentCount = specializationGroup.Count()
            })
            .OrderByDescending(x => x.AppointmentCount)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> PatientExistsAsync(
        Guid patientProfileId,
        CancellationToken cancellationToken) =>
        _dbContext.PatientProfiles.AnyAsync(
            p => p.Id == patientProfileId, cancellationToken);

    public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        _dbContext.Appointments.AnyAsync(
            a => a.PatientProfileId == patientProfileId &&
                 a.DoctorProfileId == doctorProfileId &&
                 a.AppointmentDate == date &&
                 a.Status == AppointmentStatus.Confirmed,
            cancellationToken);

    public Task<DoctorProfile?> GetDoctorProfileAsync(
        Guid doctorProfileId,
        CancellationToken cancellationToken) =>
        _dbContext.DoctorProfiles
            .Include(d => d.Specialization)
            .SingleOrDefaultAsync(d => d.Id == doctorProfileId, cancellationToken);

    public Task<DoctorProfile?> GetDoctorProfileByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _dbContext.DoctorProfiles
            .Include(d => d.Specialization)
            .SingleOrDefaultAsync(
                d => d.UserId == userId &&
                     d.ApprovalStatus == DoctorApprovalStatus.Approved,
                cancellationToken);

    public async Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(
        Guid doctorProfileId,
        DayOfWeek dayOfWeek,
        CancellationToken cancellationToken) =>
        await _dbContext.DoctorWorkingHours
            .Where(schedule =>
                schedule.DoctorProfileId == doctorProfileId &&
                schedule.DayOfWeek == dayOfWeek &&
                schedule.IsActive)
            .OrderBy(schedule => schedule.StartTime)
            .ToListAsync(cancellationToken);

    public Task<bool> IsHolidayAsync(
        Guid doctorProfileId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        _dbContext.DoctorHolidays.AnyAsync(
            holiday => holiday.DoctorProfileId == doctorProfileId &&
                       holiday.StartDate <= date &&
                       holiday.EndDate >= date,
            cancellationToken);

    public Task<bool> HasBreakOverlapAsync(
        Guid doctorProfileId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        CancellationToken cancellationToken) =>
        _dbContext.DoctorBreaks.AnyAsync(
            item => item.DoctorProfileId == doctorProfileId &&
                    item.Date == date &&
                    item.StartTime < end &&
                    item.EndTime > start,
            cancellationToken);

    public Task<bool> HasAppointmentConflictAsync(
        Guid doctorProfileId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        Guid? exceptAppointmentId,
        CancellationToken cancellationToken) =>
        _dbContext.Appointments.AnyAsync(
            item => item.DoctorProfileId == doctorProfileId &&
                    item.AppointmentDate == date &&
                    item.Id != exceptAppointmentId &&
                    item.StartTime < end &&
                    item.EndTime > start &&
                    (item.Status == AppointmentStatus.Pending ||
                     item.Status == AppointmentStatus.Confirmed),
            cancellationToken);

    public async Task AddAsync(
        Appointment appointment,
        CancellationToken cancellationToken) =>
        await _dbContext.Appointments.AddAsync(appointment, cancellationToken);

    public Task<Appointment?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        _dbContext.Appointments.SingleOrDefaultAsync(
            a => a.Id == appointmentId, cancellationToken);

    public Task<bool> HasLiveRescheduledSuccessorAsync(
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        _dbContext.Appointments.AnyAsync(
            item => item.RescheduledFromAppointmentId == appointmentId &&
                    (item.Status == AppointmentStatus.Pending ||
                     item.Status == AppointmentStatus.Confirmed),
            cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetPendingAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.Appointments
            .Include(item => item.PatientProfile)
            .Include(item => item.DoctorProfile)
            .Where(item => item.Status == AppointmentStatus.Pending)
            .OrderBy(item => item.AppointmentDate)
            .ThenBy(item => item.StartTime)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        await _dbContext.Appointments
            .Include(item => item.PatientProfile)
            .Where(item => item.DoctorProfileId == doctorProfileId &&
                           item.AppointmentDate == date &&
                           item.Status == AppointmentStatus.Confirmed)
            .OrderBy(item => item.StartTime)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PendingAppointmentDto>> GetPendingWithNamesAsync(
        CancellationToken cancellationToken) =>
        await (
            from appointment in _dbContext.Appointments
            join patient in _dbContext.PatientProfiles
                on appointment.PatientProfileId equals patient.Id
            join doctor in _dbContext.DoctorProfiles
                on appointment.DoctorProfileId equals doctor.Id
            where appointment.Status == AppointmentStatus.Pending
            orderby appointment.AppointmentDate, appointment.StartTime
            select new PendingAppointmentDto(
                appointment.Id,
                appointment.PatientProfileId,
                appointment.DoctorProfileId,
                _dbContext.Users
                    .Where(user => user.Id == patient.UserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault() ?? "Unknown patient",
                _dbContext.Users
                    .Where(user => user.Id == doctor.UserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault() ?? "Unknown doctor",
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.EndTime))
            .ToListAsync(cancellationToken);

    public Task<string?> GetPatientNameAsync(
        Guid patientProfileId,
        CancellationToken cancellationToken) =>
        _dbContext.PatientProfiles
            .Where(patient => patient.Id == patientProfileId)
            .Select(patient => _dbContext.Users
                .Where(user => user.Id == patient.UserId)
                .Select(user => user.FullName)
                .FirstOrDefault())
            .FirstOrDefaultAsync(cancellationToken);
}
