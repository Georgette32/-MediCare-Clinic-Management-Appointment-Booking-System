using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfQueueRepository(MediCareDbContext dbContext) : IQueueRepository
{
    public Task<bool> HasLiveQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        dbContext.QueueEntries.AnyAsync(item => item.AppointmentId == appointmentId &&
            (item.Status == QueueEntryStatus.Waiting ||
             item.Status == QueueEntryStatus.Called ||
             item.Status == QueueEntryStatus.InConsultation),
            cancellationToken);

    public Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        dbContext.QueueEntries.AnyAsync(item =>
            item.PatientId == patientId &&
            item.DoctorId == doctorId &&
            item.QueueDate == queueDate &&
            (item.Status == QueueEntryStatus.Waiting ||
             item.Status == QueueEntryStatus.Called ||
             item.Status == QueueEntryStatus.InConsultation),
            cancellationToken);

    public async Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken)
    {
        var currentMax = await dbContext.QueueEntries
            .Where(item => item.DoctorId == doctorId && item.QueueDate == queueDate && item.QueueSequence != null)
            .MaxAsync(item => (long?)item.QueueSequence, cancellationToken);

        return (currentMax ?? 0L) + 1L;
    }

    public Task AddAsync(QueueEntry queueEntry, CancellationToken cancellationToken) =>
        dbContext.QueueEntries.AddAsync(queueEntry, cancellationToken).AsTask();

    public async Task<IReadOnlyList<QueueEntry>> GetLiveEntriesBeforeDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Include(item => item.Appointment)
            .Where(item => item.QueueDate < date &&
                           (item.Status == QueueEntryStatus.Waiting ||
                            item.Status == QueueEntryStatus.Called ||
                            item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.QueueDate)
            .ThenBy(item => item.ArrivedAt)
            .ToListAsync(cancellationToken);

    public Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.QueueEntries
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Include(item => item.Appointment)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var attempts = await dbContext.QueueEntries
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Include(item => item.Appointment)
            .Where(item => item.AppointmentId == appointmentId)
            .ToListAsync(cancellationToken);

        return attempts.FirstOrDefault(item => item.Status.IsLive())
            ?? attempts.OrderByDescending(item => item.ArrivedAt).ThenByDescending(item => item.CreatedAt).FirstOrDefault();
    }

    public async Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .Include(item => item.Patient)
            .Include(item => item.Appointment)
            .Where(item => item.DoctorId == doctorId &&
                           item.QueueDate == queueDate &&
                           (item.Status == QueueEntryStatus.Waiting ||
                            item.Status == QueueEntryStatus.Called ||
                            item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.ArrivedAt)
            .ThenBy(item => item.QueueSequence)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .Include(item => item.Patient)
            .Include(item => item.Appointment)
            .Where(item => item.DoctorId == doctorId && item.QueueDate == queueDate &&
                           item.Source == QueueEntrySource.Scheduled &&
                           (item.Status == QueueEntryStatus.Waiting || item.Status == QueueEntryStatus.Called || item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.Appointment!.StartTime)
            .ThenBy(item => item.ArrivedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .Include(item => item.Patient)
            .Where(item => item.DoctorId == doctorId && item.QueueDate == queueDate &&
                           item.Source == QueueEntrySource.WalkIn &&
                           (item.Status == QueueEntryStatus.Waiting || item.Status == QueueEntryStatus.Called || item.Status == QueueEntryStatus.InConsultation))
            .OrderBy(item => item.ArrivedAt)
            .ThenBy(item => item.QueueSequence)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .Where(item => item.DoctorId == doctorId &&
                           item.QueueDate >= fromDate &&
                           item.QueueDate <= toDate &&
                           item.Status == QueueEntryStatus.Completed &&
                           item.ConsultationStartedAt != null &&
                           item.CompletedAt != null)
            .OrderBy(item => item.CompletedAt)
            .ToListAsync(cancellationToken);
}
