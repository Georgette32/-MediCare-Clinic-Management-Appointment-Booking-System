using MediCare.Application.Common;
using MediCare.Application.DTOs.Prescriptions;

namespace MediCare.Application.Interfaces.Services;

public interface IPrescriptionService
{
    // Doctor side. An issued prescription cannot be edited; it can only be deleted
    // by the doctor who wrote it.
    Task<Result<PrescriptionDto>> CreateAsync(Guid doctorUserId, CreatePrescriptionRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PrescriptionDto>>> GetPatientPrescriptionsForDoctorAsync(Guid doctorUserId, Guid patientProfileId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid doctorUserId, Guid prescriptionId, CancellationToken cancellationToken = default);

    // Patient side.
    Task<Result<IReadOnlyList<PrescriptionDto>>> GetMyPrescriptionsAsync(Guid patientUserId, CancellationToken cancellationToken = default);

    Task<Result<PrescriptionDto>> GetMyPrescriptionAsync(Guid patientUserId, Guid prescriptionId, CancellationToken cancellationToken = default);
}
