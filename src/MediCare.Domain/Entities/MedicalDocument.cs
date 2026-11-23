using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

public sealed class MedicalDocument : BaseEntity
{
    private MedicalDocument()
    {
        // Required by EF Core.
    }

    public MedicalDocument(
        Guid patientProfileId,
        Guid? medicalRecordId,
        Guid uploadedByUserId,
        string originalFileName,
        string storedFileName,
        string contentType,
        long sizeInBytes)
    {
        if (patientProfileId == Guid.Empty)
        {
            throw new DomainException("A document must belong to a patient.");
        }

        if (medicalRecordId == Guid.Empty)
        {
            throw new DomainException("The medical record id is not valid.");
        }

        if (uploadedByUserId == Guid.Empty)
        {
            throw new DomainException("A document must have an uploader.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new DomainException("The file name is required.");
        }

        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            throw new DomainException("The stored file name is required.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainException("The content type is required.");
        }

        if (sizeInBytes <= 0)
        {
            throw new DomainException("The file cannot be empty.");
        }

        PatientProfileId = patientProfileId;
        MedicalRecordId = medicalRecordId;
        UploadedByUserId = uploadedByUserId;
        OriginalFileName = originalFileName.Trim();
        StoredFileName = storedFileName.Trim();
        ContentType = contentType.Trim();
        SizeInBytes = sizeInBytes;
    }

    public Guid PatientProfileId { get; private set; }

    public Guid? MedicalRecordId { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    // The name the user uploaded. Display only, never used as a path on disk.
    public string OriginalFileName { get; private set; } = string.Empty;

    // The generated name (for example a GUID) used by the file storage.
    public string StoredFileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeInBytes { get; private set; }

    public bool IsDeleted { get; private set; }

    public void Delete()
    {
        IsDeleted = true;
        MarkUpdated();
    }
}
