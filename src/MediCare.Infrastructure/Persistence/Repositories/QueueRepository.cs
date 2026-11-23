using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class QueueRepository : IQueueRepository
{
    private readonly ApplicationDbContext _dbContext;

    public QueueRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> HasQueueEntryForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        _dbContext.QueueEntries.AnyAsync(
            item => item.AppointmentId == appointmentId, cancellationToken);

    public Task<bool> HasActiveEntryForPatientAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken) =>
        _dbContext.QueueEntries.AnyAsync(
            item => item.PatientProfileId == patientProfileId &&
                    item.DoctorProfileId == doctorProfileId &&
                    item.QueueDate == queueDate &&
                    (item.Status == QueueEntryStatus.Waiting ||
                     item.Status == QueueEntryStatus.Called ||
                     item.Status == QueueEntryStatus.InConsultation),
            cancellationToken);

    public async Task<long> GetNextWalkInSequenceAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken)
    {
        var currentMax = await _dbContext.QueueEntries
            .Where(item => item.DoctorProfileId == doctorProfileId &&
                           item.QueueDate == queueDate &&
                           item.QueueSequence != null)
            .MaxAsync(item => (long?)item.QueueSequence, cancellationToken);

        return (currentMax ?? 0L) + 1L;
    }

    public async Task AddAsync(
        QueueEntry queueEntry,
        CancellationToken cancellationToken) =>
        await _dbContext.QueueEntries.AddAsync(queueEntry, cancellationToken);

    public Task<QueueEntry?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        QueryWithDetails()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<QueueEntry?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        QueryWithDetails()
            .SingleOrDefaultAsync(item => item.AppointmentId == appointmentId, cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken) =>
        await _dbContext.QueueEntries
            .Include(item => item.PatientProfile)
            .Include(item => item.Appointment)
            .Where(item => item.DoctorProfileId == doctorProfileId &&
                           item.QueueDate == queueDate &&
                           (item.Status == QueueEntryStatus.Waiting ||
                            item.Status == QueueEntryStatus.Called ||
                            item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.ArrivedAt)
            .ThenBy(item => item.QueueSequence)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken) =>
        await _dbContext.QueueEntries
            .Include(item => item.PatientProfile)
            .Include(item => item.Appointment)
            .Where(item => item.DoctorProfileId == doctorProfileId &&
                           item.QueueDate == queueDate &&
                           item.Source == QueueEntrySource.Scheduled &&
                           (item.Status == QueueEntryStatus.Waiting ||
                            item.Status == QueueEntryStatus.Called ||
                            item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.Appointment!.StartTime)
            .ThenBy(item => item.ArrivedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken) =>
        await _dbContext.QueueEntries
            .Include(item => item.PatientProfile)
            .Where(item => item.DoctorProfileId == doctorProfileId &&
                           item.QueueDate == queueDate &&
                           item.Source == QueueEntrySource.WalkIn &&
                           (item.Status == QueueEntryStatus.Waiting ||
                            item.Status == QueueEntryStatus.Called ||
                            item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.ArrivedAt)
            .ThenBy(item => item.QueueSequence)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(
        Guid doctorProfileId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken) =>
        await _dbContext.QueueEntries
            .Where(item => item.DoctorProfileId == doctorProfileId &&
                           item.QueueDate >= fromDate &&
                           item.QueueDate <= toDate &&
                           item.Status == QueueEntryStatus.Completed &&
                           item.ConsultationStartedAt != null &&
                           item.CompletedAt != null)
            .OrderBy(item => item.CompletedAt)
            .ToListAsync(cancellationToken);

    private IQueryable<QueueEntry> QueryWithDetails() =>
        _dbContext.QueueEntries
            .Include(item => item.PatientProfile)
            .Include(item => item.DoctorProfile)
            .Include(item => item.Appointment);
}
