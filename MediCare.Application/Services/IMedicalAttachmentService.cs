namespace MediCare.Application.Services;

/// <summary>
/// Medical attachments: upload by a treating doctor and authorized download. Access uses MedicalRecordAccessPolicy and is
/// re-checked on every call; ids come from the request but ownership is always resolved on the server.
/// </summary>
public interface IMedicalAttachmentService
{
    Task<MedicalActionResult> UploadAsync(Guid recordId, Guid doctorUserId, MedicalFileUpload upload, CancellationToken cancellationToken);

    /// <summary>The file to stream, or null when the viewer may not see it (or it does not exist).</summary>
    Task<MedicalAttachmentDownload?> GetForDownloadAsync(Guid attachmentId, Guid viewerUserId, MedicalViewerRole role, CancellationToken cancellationToken);
}

/// <summary>Uploaded content. The stream is read at most once and up to the configured limit plus one byte.</summary>
public sealed record MedicalFileUpload(Stream Content, string? OriginalFileName, string? Description);

/// <summary>The caller owns and disposes <see cref="Content"/>.</summary>
public sealed record MedicalAttachmentDownload(string FileName, string ContentType, long SizeBytes, Stream Content);

public sealed record MedicalAttachmentView(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Description,
    string UploadedByName,
    DateTimeOffset UploadedAtUtc);
