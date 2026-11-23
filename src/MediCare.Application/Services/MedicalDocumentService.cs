using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.DTOs.Documents;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Interfaces.Storage;
using MediCare.Application.Mappings;
using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Application.Services;

public sealed class MedicalDocumentService : IMedicalDocumentService
{
    private const string NotFoundMessage = "Document was not found.";
    private const string DoctorOnlyMessage = "Only approved doctors can manage patient documents.";
    private const string NoAccessMessage = "You do not have access to this patient's documents.";
    private const string PatientNotFoundMessage = "Patient profile was not found.";

    private readonly IMedicalDocumentRepository _documents;
    private readonly IMedicalRecordRepository _medicalRecords;
    private readonly IDoctorRepository _doctors;
    private readonly IPatientRepository _patients;
    private readonly IFileStorage _storage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<UploadMedicalDocumentRequest> _uploadValidator;

    public MedicalDocumentService(
        IMedicalDocumentRepository documents,
        IMedicalRecordRepository medicalRecords,
        IDoctorRepository doctors,
        IPatientRepository patients,
        IFileStorage storage,
        IUnitOfWork unitOfWork,
        IValidator<UploadMedicalDocumentRequest> uploadValidator)
    {
        _documents = documents;
        _medicalRecords = medicalRecords;
        _doctors = doctors;
        _patients = patients;
        _storage = storage;
        _unitOfWork = unitOfWork;
        _uploadValidator = uploadValidator;
    }

    // ---------------------------------------------------------------- patient

    public async Task<Result<MedicalDocumentDto>> UploadAsPatientAsync(Guid patientUserId, UploadMedicalDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var patient = await GetPatientAsync(patientUserId, cancellationToken);
        if (patient is null)
        {
            return Result<MedicalDocumentDto>.Fail(PatientNotFoundMessage);
        }

        return await StoreAsync(patientUserId, patient.Id, request, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<MedicalDocumentDto>>> GetMyDocumentsAsync(Guid patientUserId, CancellationToken cancellationToken = default)
    {
        var patient = await GetPatientAsync(patientUserId, cancellationToken);
        if (patient is null)
        {
            return Result<IReadOnlyList<MedicalDocumentDto>>.Fail(PatientNotFoundMessage);
        }

        var documents = await _documents.GetByPatientAsync(patient.Id, cancellationToken);
        IReadOnlyList<MedicalDocumentDto> items = documents.Select(d => d.ToDto(patientUserId)).ToList();
        return Result<IReadOnlyList<MedicalDocumentDto>>.Ok(items);
    }

    public async Task<Result<MedicalDocumentFileDto>> DownloadMyDocumentAsync(Guid patientUserId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var patient = await GetPatientAsync(patientUserId, cancellationToken);
        if (patient is null)
        {
            return Result<MedicalDocumentFileDto>.Fail(PatientNotFoundMessage);
        }

        var document = await _documents.GetByIdAsync(documentId, false, cancellationToken);

        // Same message for "missing" and "belongs to someone else".
        if (document is null || document.IsDeleted || document.PatientProfileId != patient.Id)
        {
            return Result<MedicalDocumentFileDto>.Fail(NotFoundMessage);
        }

        return await OpenAsync(document, cancellationToken);
    }

    public async Task<Result> DeleteMyDocumentAsync(Guid patientUserId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var patient = await GetPatientAsync(patientUserId, cancellationToken);
        if (patient is null)
        {
            return Result.Fail(PatientNotFoundMessage);
        }

        var document = await _documents.GetByIdAsync(documentId, true, cancellationToken);
        if (document is null || document.IsDeleted || document.PatientProfileId != patient.Id)
        {
            return Result.Fail(NotFoundMessage);
        }

        if (document.UploadedByUserId != patientUserId)
        {
            return Result.Fail("You can only delete documents that you uploaded.");
        }

        document.Delete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    // ----------------------------------------------------------------- doctor

    public async Task<Result<MedicalDocumentDto>> UploadAsDoctorAsync(Guid doctorUserId, Guid patientProfileId, UploadMedicalDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<MedicalDocumentDto>.Fail(DoctorOnlyMessage);
        }

        if (!await HasAccessAsync(doctor.Id, patientProfileId, cancellationToken))
        {
            return Result<MedicalDocumentDto>.Fail(NoAccessMessage);
        }

        return await StoreAsync(doctorUserId, patientProfileId, request, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<MedicalDocumentDto>>> GetPatientDocumentsForDoctorAsync(Guid doctorUserId, Guid patientProfileId, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<IReadOnlyList<MedicalDocumentDto>>.Fail(DoctorOnlyMessage);
        }

        if (!await HasAccessAsync(doctor.Id, patientProfileId, cancellationToken))
        {
            return Result<IReadOnlyList<MedicalDocumentDto>>.Fail(NoAccessMessage);
        }

        var documents = await _documents.GetByPatientAsync(patientProfileId, cancellationToken);
        IReadOnlyList<MedicalDocumentDto> items = documents.Select(d => d.ToDto(doctorUserId)).ToList();
        return Result<IReadOnlyList<MedicalDocumentDto>>.Ok(items);
    }

    public async Task<Result<MedicalDocumentFileDto>> DownloadAsDoctorAsync(Guid doctorUserId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<MedicalDocumentFileDto>.Fail(DoctorOnlyMessage);
        }

        var document = await _documents.GetByIdAsync(documentId, false, cancellationToken);
        if (document is null ||
            document.IsDeleted ||
            !await HasAccessAsync(doctor.Id, document.PatientProfileId, cancellationToken))
        {
            return Result<MedicalDocumentFileDto>.Fail(NotFoundMessage);
        }

        return await OpenAsync(document, cancellationToken);
    }

    public async Task<Result> DeleteAsDoctorAsync(Guid doctorUserId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail(DoctorOnlyMessage);
        }

        var document = await _documents.GetByIdAsync(documentId, true, cancellationToken);
        if (document is null ||
            document.IsDeleted ||
            !await HasAccessAsync(doctor.Id, document.PatientProfileId, cancellationToken))
        {
            return Result.Fail(NotFoundMessage);
        }

        if (document.UploadedByUserId != doctorUserId)
        {
            return Result.Fail("You can only delete documents that you uploaded.");
        }

        document.Delete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Result<MedicalDocumentDto>> StoreAsync(
        Guid uploaderUserId,
        Guid patientProfileId,
        UploadMedicalDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<MedicalDocumentDto>.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        // Never trust the client's path or content type: keep only the file name and
        // derive everything else from the extension and the real bytes.
        var originalName = Path.GetFileName(request.FileName.Trim());
        var extension = Path.GetExtension(originalName).ToLowerInvariant();

        var content = await ReadLimitedAsync(request.Content, MedicalDocumentRules.MaxSizeInBytes, cancellationToken);
        if (content is null)
        {
            return Result<MedicalDocumentDto>.Fail("The file cannot be larger than 5 MB.");
        }

        if (content.Length == 0)
        {
            return Result<MedicalDocumentDto>.Fail("The file is empty.");
        }

        if (!MedicalDocumentRules.HasValidSignature(extension, content))
        {
            return Result<MedicalDocumentDto>.Fail("The file content does not match its type.");
        }

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var contentType = MedicalDocumentRules.ContentTypeFor(extension) ?? "application/octet-stream";

        MedicalDocument document;
        try
        {
            document = new MedicalDocument(
                patientProfileId,
                null,
                uploaderUserId,
                originalName,
                storedName,
                contentType,
                content.Length);
        }
        catch (DomainException ex)
        {
            return Result<MedicalDocumentDto>.Fail(ex.Message);
        }

        await using (var buffer = new MemoryStream(content))
        {
            await _storage.SaveAsync(storedName, buffer, cancellationToken);
        }

        try
        {
            await _documents.AddAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Do not leave an orphan file behind when the database save fails.
            await _storage.DeleteAsync(storedName, CancellationToken.None);
            throw;
        }

        return Result<MedicalDocumentDto>.Ok(document.ToDto(uploaderUserId));
    }

    private async Task<Result<MedicalDocumentFileDto>> OpenAsync(MedicalDocument document, CancellationToken cancellationToken)
    {
        var stream = await _storage.OpenReadAsync(document.StoredFileName, cancellationToken);
        if (stream is null)
        {
            return Result<MedicalDocumentFileDto>.Fail("The file is no longer available.");
        }

        return Result<MedicalDocumentFileDto>.Ok(new MedicalDocumentFileDto
        {
            Content = stream,
            FileName = document.OriginalFileName,
            ContentType = document.ContentType
        });
    }

    private async Task<PatientProfile?> GetPatientAsync(Guid patientUserId, CancellationToken cancellationToken)
    {
        if (patientUserId == Guid.Empty)
        {
            return null;
        }

        return await _patients.GetByUserIdAsync(patientUserId, false, cancellationToken);
    }

    private async Task<DoctorProfile?> GetApprovedDoctorAsync(Guid doctorUserId, CancellationToken cancellationToken)
    {
        if (doctorUserId == Guid.Empty)
        {
            return null;
        }

        var doctor = await _doctors.GetByUserIdAsync(doctorUserId, false, cancellationToken);
        return doctor is { IsApproved: true } ? doctor : null;
    }

    private Task<bool> HasAccessAsync(Guid doctorProfileId, Guid patientProfileId, CancellationToken cancellationToken) =>
        _medicalRecords.DoctorHasCompletedAppointmentWithPatientAsync(
            doctorProfileId, patientProfileId, null, cancellationToken);

    // Reads at most maxBytes. Returns null when the stream is larger than that, so a
    // client that lies about the size cannot make the server buffer a huge file.
    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
