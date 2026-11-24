using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using Microsoft.Extensions.Options;

namespace MediCare.Application.Services;

public sealed class MedicalAttachmentService(
    IMedicalRecordRepository records,
    IMedicalAttachmentRepository attachments,
    IAppointmentRepository appointments,
    IMedicalFileStore fileStore,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IOptions<MedicalFileOptions> options) : IMedicalAttachmentService
{
    private const string NotFoundMessage = "The record was not found or is not available to you.";
    private const int CopyBufferBytes = 81920;

    public async Task<MedicalActionResult> UploadAsync(Guid recordId, Guid doctorUserId, MedicalFileUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return Fail("Your account is not linked to an active doctor profile.");

        var description = string.IsNullOrWhiteSpace(upload.Description) ? null : upload.Description.Trim();
        if (description is not null && description.Length > MedicalAttachment.MaxDescriptionLength)
            return Fail($"The description cannot exceed {MedicalAttachment.MaxDescriptionLength} characters.");

        // Access is checked before any byte is written; the transaction below checks it again.
        var viewer = new MedicalViewer(MedicalViewerRole.Doctor, null, doctor.Id);
        if (!await MayUploadAsync(recordId, viewer, cancellationToken))
            return Fail(NotFoundMessage);

        var maxBytes = options.Value.MaxFileSizeBytes;
        var content = await ReadUpToAsync(upload.Content, maxBytes + 1, cancellationToken);
        var validation = MedicalFileValidator.Validate(content, maxBytes);
        if (!validation.IsValid)
            return Fail(validation.Error!);

        var storedFileName = MedicalAttachment.CreateStoredFileName(validation.ContentType!);
        await using (var source = new MemoryStream(content, writable: false))
        {
            var saved = await fileStore.SaveAsync(source, storedFileName, cancellationToken);
            MedicalActionResult result;
            try
            {
                result = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
                {
                    var record = await records.GetRecordByIdAsync(recordId, ct);
                    if (record is null)
                        return Fail(NotFoundMessage);
                    var related = await records.HasTreatingRelationshipAsync(doctor.Id, record.PatientId, ct);
                    if (!MedicalRecordAccessPolicy.CanUploadAttachment(viewer, related))
                        return Fail(NotFoundMessage);

                    var attachment = MedicalAttachment.Create(
                        record.Id,
                        doctor.Id,
                        upload.OriginalFileName,
                        validation.ContentType!,
                        saved.SizeBytes,
                        storedFileName,
                        saved.Sha256Hex,
                        description,
                        clinicClock.UtcNow);
                    record.AddAttachment(attachment);
                    return Ok("Attachment uploaded.", attachment.Id);
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or UniqueConstraintConflictException)
            {
                await DeleteWrittenFileAsync(storedFileName, cancellationToken);
                return Fail(exception.Message);
            }
            catch (Exception)
            {
                // Compensation: the database write failed after the file was written, so the orphaned file is removed.
                await DeleteWrittenFileAsync(storedFileName, cancellationToken);
                throw;
            }

            if (!result.Succeeded)
                await DeleteWrittenFileAsync(storedFileName, cancellationToken);
            return result;
        }
    }

    public async Task<MedicalAttachmentDownload?> GetForDownloadAsync(Guid attachmentId, Guid viewerUserId, MedicalViewerRole role, CancellationToken cancellationToken)
    {
        var attachment = await attachments.GetByIdAsync(attachmentId, cancellationToken);
        if (attachment is null)
            return null;

        var viewer = await ResolveViewerAsync(viewerUserId, role, cancellationToken);
        var record = await records.GetRecordByIdAsync(attachment.MedicalRecordId, cancellationToken);
        if (record is null)
            return null;

        var related = viewer.Role == MedicalViewerRole.Doctor && viewer.DoctorId is Guid doctorId
            && await records.HasTreatingRelationshipAsync(doctorId, record.PatientId, cancellationToken);
        if (!MedicalRecordAccessPolicy.CanViewPatientHistory(viewer, record.PatientId, related))
            return null;

        var stream = await fileStore.OpenReadAsync(attachment.StoredFileName, cancellationToken);
        return new MedicalAttachmentDownload(attachment.OriginalFileName, attachment.ContentType, attachment.SizeBytes, stream);
    }

    private async Task<bool> MayUploadAsync(Guid recordId, MedicalViewer viewer, CancellationToken cancellationToken)
    {
        var record = await records.GetRecordByIdAsync(recordId, cancellationToken);
        if (record is null || viewer.DoctorId is not Guid doctorId)
            return false;
        var related = await records.HasTreatingRelationshipAsync(doctorId, record.PatientId, cancellationToken);
        return MedicalRecordAccessPolicy.CanUploadAttachment(viewer, related);
    }

    private async Task<MedicalViewer> ResolveViewerAsync(Guid userId, MedicalViewerRole role, CancellationToken cancellationToken) => role switch
    {
        MedicalViewerRole.Patient => new MedicalViewer(role, await records.FindPatientIdForUserAsync(userId, cancellationToken), null),
        MedicalViewerRole.Doctor => new MedicalViewer(role, null, (await appointments.GetDoctorByApplicationUserIdAsync(userId, cancellationToken))?.Id),
        _ => new MedicalViewer(role, null, null)
    };

    /// <summary>Reads at most <paramref name="limit"/> bytes. A stream longer than the limit yields exactly limit bytes, which validation then rejects.</summary>
    private static async Task<byte[]> ReadUpToAsync(Stream source, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[CopyBufferBytes];
        while (buffer.Length < limit)
        {
            var wanted = (int)Math.Min(chunk.Length, limit - buffer.Length);
            var read = await source.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken);
            if (read == 0)
                break;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private async Task DeleteWrittenFileAsync(string storedFileName, CancellationToken cancellationToken)
    {
        try
        {
            await fileStore.DeleteAsync(storedFileName, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: the stored name is unreferenced, so a leftover file is harmless and can be swept later.
        }
    }

    private static MedicalActionResult Ok(string message, Guid attachmentId) => new(true, message, attachmentId);

    private static MedicalActionResult Fail(string message) => new(false, message);
}
