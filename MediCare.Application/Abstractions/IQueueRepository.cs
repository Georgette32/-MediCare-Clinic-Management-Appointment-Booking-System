using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IQueueRepository
{
    Task<bool> HasQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task AddAsync(QueueEntry queueEntry, CancellationToken cancellationToken);
    Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
