using MediCare.Application.Common;
using MediCare.Application.DTOs.Documents;

namespace MediCare.Application.Interfaces.Services;

public interface IMedicalDocumentService
{
    // Patient side: the patient's own documents only.
    Task<Result<MedicalDocumentDto>> UploadAsPatientAsync(Guid patientUserId, UploadMedicalDocumentRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MedicalDocumentDto>>> GetMyDocumentsAsync(Guid patientUserId, CancellationToken cancellationToken = default);

    Task<Result<MedicalDocumentFileDto>> DownloadMyDocumentAsync(Guid patientUserId, Guid documentId, CancellationToken cancellationToken = default);

    Task<Result> DeleteMyDocumentAsync(Guid patientUserId, Guid documentId, CancellationToken cancellationToken = default);

    // Doctor side: only patients the doctor has a completed appointment with.
    Task<Result<MedicalDocumentDto>> UploadAsDoctorAsync(Guid doctorUserId, Guid patientProfileId, UploadMedicalDocumentRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MedicalDocumentDto>>> GetPatientDocumentsForDoctorAsync(Guid doctorUserId, Guid patientProfileId, CancellationToken cancellationToken = default);

    Task<Result<MedicalDocumentFileDto>> DownloadAsDoctorAsync(Guid doctorUserId, Guid documentId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsDoctorAsync(Guid doctorUserId, Guid documentId, CancellationToken cancellationToken = default);
}
