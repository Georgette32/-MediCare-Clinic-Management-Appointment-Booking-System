using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

/// <summary>
/// Read-only analytics queries. Every method is a single AsNoTracking projection. A null doctor set means all doctors.
/// </summary>
public sealed class EfAnalyticsRepository(MediCareDbContext dbContext) : IAnalyticsRepository
{
    public async Task<IReadOnlyList<AnalyticsDoctor>> GetDoctorsAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.Doctors.AsNoTracking()
            .Where(doctor => ids == null || ids.Contains(doctor.Id))
            .OrderBy(doctor => doctor.FullName)
            .Select(doctor => new AnalyticsDoctor(doctor.Id, doctor.ApplicationUserId, doctor.FullName, doctor.AppointmentDurationMinutes, doctor.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<Guid?> GetDoctorIdForApplicationUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        dbContext.Doctors.AsNoTracking()
            .Where(doctor => doctor.ApplicationUserId == applicationUserId)
            .Select(doctor => (Guid?)doctor.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AnalyticsVisitRow>> GetVisitsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.QueueEntries.AsNoTracking()
            .Where(entry => entry.QueueDate >= fromDate && entry.QueueDate <= toDate && (ids == null || ids.Contains(entry.DoctorId)))
            .Select(entry => new AnalyticsVisitRow(
                entry.DoctorId,
                entry.PatientId,
                entry.QueueDate,
                entry.Source,
                entry.Status,
                entry.ArrivedAt,
                entry.CalledAt,
                entry.ConsultationStartedAt,
                entry.CompletedAt,
                entry.AppointmentId != null && entry.Appointment!.CancellationReason == AppointmentCancellationReasons.ClinicUnavailable,
                entry.AppointmentId != null ? entry.Appointment!.AppointmentDate : (DateOnly?)null,
                entry.AppointmentId != null ? entry.Appointment!.StartTime : (TimeOnly?)null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsAppointmentRow>> GetAppointmentsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.Appointments.AsNoTracking()
            .Where(item => item.AppointmentDate >= fromDate && item.AppointmentDate <= toDate && (ids == null || ids.Contains(item.DoctorId)))
            .Select(item => new AnalyticsAppointmentRow(
                item.DoctorId,
                item.AppointmentDate,
                item.StartTime,
                item.Status,
                item.CancellationReason == AppointmentCancellationReasons.ClinicUnavailable))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsWorkingHourRow>> GetActiveWorkingHoursAsync(IReadOnlyCollection<Guid>? doctorIds, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.DoctorWorkingHours.AsNoTracking()
            .Where(item => item.IsActive && (ids == null || ids.Contains(item.DoctorId)))
            .Select(item => new AnalyticsWorkingHourRow(item.DoctorId, item.DayOfWeek, item.StartTime, item.EndTime))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsHolidayRow>> GetHolidaysAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.DoctorHolidays.AsNoTracking()
            .Where(item => item.StartDate <= toDate && item.EndDate >= fromDate && (ids == null || ids.Contains(item.DoctorId)))
            .Select(item => new AnalyticsHolidayRow(item.DoctorId, item.StartDate, item.EndDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsExtensionRow>> GetApprovedExtensionsAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.DoctorWorkingHourExtensions.AsNoTracking()
            .Where(item => item.Status == WorkingHourExtensionStatus.Approved &&
                           item.Date >= fromDate && item.Date <= toDate &&
                           (ids == null || ids.Contains(item.DoctorId)))
            .Select(item => new AnalyticsExtensionRow(item.DoctorId, item.Date, item.OriginalEndTime, item.ExtendedEndTime))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsServiceHistoryRow>> GetServiceHistoryAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.QueueEntries.AsNoTracking()
            .Where(entry => entry.QueueDate >= fromDate && entry.QueueDate <= toDate &&
                            (ids == null || ids.Contains(entry.DoctorId)) &&
                            entry.Status == QueueEntryStatus.Completed &&
                            entry.ConsultationStartedAt != null && entry.CompletedAt != null &&
                            !(entry.AppointmentId != null && entry.Appointment!.CancellationReason == AppointmentCancellationReasons.ClinicUnavailable))
            .Select(entry => new AnalyticsServiceHistoryRow(entry.DoctorId, entry.QueueDate, entry.ConsultationStartedAt!.Value, entry.CompletedAt!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsBreakRow>> GetBreaksAsync(IReadOnlyCollection<Guid>? doctorIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var ids = doctorIds?.ToArray();
        return await dbContext.DoctorBreaks.AsNoTracking()
            .Where(item => item.Date >= fromDate && item.Date <= toDate && (ids == null || ids.Contains(item.DoctorId)))
            .Select(item => new AnalyticsBreakRow(item.DoctorId, item.Date, item.StartTime, item.EndTime))
            .ToListAsync(cancellationToken);
    }
}
