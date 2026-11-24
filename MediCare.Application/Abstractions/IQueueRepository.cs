using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IQueueRepository
{
    /// <summary>True when the appointment already has an attendance attempt that is Waiting, Called or InConsultation.</summary>
    Task<bool> HasLiveQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task AddAsync(QueueEntry queueEntry, CancellationToken cancellationToken);
    Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>
    /// Returns the live attendance attempt for the appointment when one exists; otherwise the most recently
    /// arrived attempt (which may be Cancelled, NoShow or Completed). Null when the appointment has no attempts.
    /// </summary>
    Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>
    /// Live attendance attempts (Waiting, Called or InConsultation) whose clinic date is before the given date, with
    /// the patient, doctor and appointment loaded. Used by the stale-queue sweep.
    /// </summary>
    Task<IReadOnlyList<QueueEntry>> GetLiveEntriesBeforeDateAsync(DateOnly date, CancellationToken cancellationToken);
}
