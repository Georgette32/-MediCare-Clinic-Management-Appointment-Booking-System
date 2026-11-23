using MediCare.Application.DTOs.Documents;
using MediCare.Domain.Entities;

namespace MediCare.Application.Mappings;

internal static class MedicalDocumentMappings
{
    public static MedicalDocumentDto ToDto(this MedicalDocument document, Guid requesterUserId) => new()
    {
        Id = document.Id,
        PatientProfileId = document.PatientProfileId,
        MedicalRecordId = document.MedicalRecordId,
        OriginalFileName = document.OriginalFileName,
        ContentType = document.ContentType,
        SizeInBytes = document.SizeInBytes,
        UploadedAtUtc = document.CreatedAtUtc,
        CanDelete = document.UploadedByUserId == requesterUserId
    };
}
