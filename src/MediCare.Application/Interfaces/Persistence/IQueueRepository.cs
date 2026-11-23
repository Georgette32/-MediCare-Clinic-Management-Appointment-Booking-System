using MediCare.Domain.Entities;

namespace MediCare.Application.Interfaces.Persistence;

public interface IQueueRepository
{
    Task<bool> HasQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);

    Task<bool> HasActiveEntryForPatientAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken);

    Task<long> GetNextWalkInSequenceAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken);

    Task AddAsync(QueueEntry queueEntry, CancellationToken cancellationToken);

    Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(
        Guid doctorProfileId,
        DateOnly queueDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(
        Guid doctorProfileId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken);
}
