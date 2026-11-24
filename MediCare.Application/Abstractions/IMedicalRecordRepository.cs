using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Abstractions;

public interface IMedicalRecordRepository
{
    /// <summary>The Patient profile linked to the Identity user, or null.</summary>
    Task<Guid?> FindPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken);

    /// <summary>Tracked queue entry with its patient and appointment loaded.</summary>
    Task<QueueEntry?> GetQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken);

    /// <summary>Tracked record of the attendance attempt, or null.</summary>
    Task<MedicalRecord?> GetRecordByQueueEntryIdAsync(Guid queueEntryId, CancellationToken cancellationToken);

    /// <summary>Tracked record with patient, doctor, specialization, prescription items and addenda loaded.</summary>
    Task<MedicalRecord?> GetRecordByIdAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>Records of one patient with the same loads as <see cref="GetRecordByIdAsync"/>, newest visit first.</summary>
    Task<IReadOnlyList<MedicalRecord>> GetRecordsForPatientAsync(Guid patientId, CancellationToken cancellationToken);

    Task<string?> GetPatientNameAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>True when the doctor has an In-Consultation or Completed queue entry, or a Confirmed or Completed appointment, with the patient.</summary>
    Task<bool> HasTreatingRelationshipAsync(Guid doctorId, Guid patientId, CancellationToken cancellationToken);

    /// <summary>The doctor's attendance attempts for the clinic date that are In Consultation or Completed, oldest first.</summary>
    Task<IReadOnlyList<DoctorVisitSummary>> GetDoctorVisitsForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    Task AddRecordAsync(MedicalRecord record, CancellationToken cancellationToken);
}
