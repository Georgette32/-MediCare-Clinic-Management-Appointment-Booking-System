using MediCare.Domain.Entities;

namespace MediCare.Application.Interfaces.Persistence;

public interface IMedicalRecordRepository
{
    Task<MedicalRecord?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the patient's records that are not deleted, newest first.
    /// </summary>
    Task<IReadOnlyList<MedicalRecord>> GetByPatientAsync(Guid patientProfileId, CancellationToken cancellationToken = default);

    Task AddAsync(MedicalRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the doctor has a completed appointment with the patient. When an
    /// appointment id is supplied, that exact appointment must belong to the pair
    /// and be completed.
    /// </summary>
    Task<bool> DoctorHasCompletedAppointmentWithPatientAsync(
        Guid doctorProfileId,
        Guid patientProfileId,
        Guid? appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patients who have at least one completed appointment with the doctor.
    /// </summary>
    Task<IReadOnlyList<PatientProfile>> GetPatientsWithCompletedAppointmentsAsync(
        Guid doctorProfileId,
        CancellationToken cancellationToken = default);
}
