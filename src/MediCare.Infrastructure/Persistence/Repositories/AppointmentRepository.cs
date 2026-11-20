
using MediCare.Application.DTOs.Analytics;
using MediCare.Application.Interfaces.Persistence;
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
            a.Status != AppointmentStatus.NoShow,
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
            query = query.Where(a => a.Status == status.Value);

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

    public async Task<IReadOnlyList<SpecializationAnalyticsDto>>
        GetPopularSpecializationsAsync(
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
}