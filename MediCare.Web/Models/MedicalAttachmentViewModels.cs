using MediCare.Application.Services;

namespace MediCare.Web.Models;

/// <summary>What the attachments partial needs: the record with its files, and where an upload should return to.</summary>
public sealed record MedicalAttachmentPanel(MedicalRecordView Record, Guid? QueueEntryId = null, Guid? PatientId = null);

/// <summary>
/// Request limits for the upload action. The attributes need constants, so the request ceiling is the default
/// MedicalFiles:MaxFileSizeBytes (10 MB) plus form overhead. The service enforces the configured maximum; raising the
/// configured value above 10 MB requires raising these constants as well.
/// </summary>
public static class MedicalAttachmentLimits
{
    public const long DefaultMaxFileBytes = 10L * 1024 * 1024;
    public const long MaxRequestBytes = DefaultMaxFileBytes + 1024L * 1024;
}
