using MediCare.Application.Common;
using MediCare.Application.DTOs.MedicalRecords;

namespace MediCare.Application.Interfaces.Services;

public interface IMedicalRecordService
{
    // Doctor side.
    Task<Result<IReadOnlyList<DoctorPatientDto>>> GetMyPatientsAsync(Guid doctorUserId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MedicalRecordDto>>> GetPatientRecordsForDoctorAsync(Guid doctorUserId, Guid patientProfileId, CancellationToken cancellationToken = default);

    Task<Result<MedicalRecordDto>> CreateAsync(Guid doctorUserId, CreateMedicalRecordRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid doctorUserId, UpdateMedicalRecordRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid doctorUserId, Guid recordId, CancellationToken cancellationToken = default);

    // Patient side.
    Task<Result<IReadOnlyList<MedicalRecordDto>>> GetMyRecordsAsync(Guid patientUserId, CancellationToken cancellationToken = default);

    Task<Result<MedicalRecordDto>> GetMyRecordAsync(Guid patientUserId, Guid recordId, CancellationToken cancellationToken = default);
}
