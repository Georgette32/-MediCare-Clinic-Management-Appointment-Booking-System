using System.Text.RegularExpressions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Metadata of a file attached to a medical record (lab results, scans). The bytes live on disk outside wwwroot and are
/// served only through an authorized action. Attachments are append-only: there is no delete.
/// </summary>
public sealed class MedicalAttachment
{
    public const int MaxOriginalFileNameLength = 255;
    public const int MaxDescriptionLength = 500;
    public const int StoredFileNameLength = 36;
    public const string ContentTypePdf = "application/pdf";
    public const string ContentTypeJpeg = "image/jpeg";
    public const string ContentTypePng = "image/png";

    private static readonly Regex StoredFileNamePattern = new(@"^[0-9a-f]{32}\.(pdf|jpg|png)$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private MedicalAttachment() { }

    public Guid Id { get; private set; }
    public Guid MedicalRecordId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoredFileName { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid UploadedByDoctorId { get; private set; }
    public DateTimeOffset UploadedAtUtc { get; private set; }

    public Doctor UploadedByDoctor { get; private set; } = null!;

    public static MedicalAttachment Create(
        Guid medicalRecordId,
        Guid uploadedByDoctorId,
        string? originalFileName,
        string contentType,
        long sizeBytes,
        string storedFileName,
        string sha256,
        string? description,
        DateTimeOffset uploadedAtUtc)
    {
        if (!IsSupportedContentType(contentType))
            throw new ArgumentException("Only PDF, JPEG and PNG files can be attached.", nameof(contentType));
        if (sizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "An attachment must not be empty.");
        if (!IsValidStoredFileName(storedFileName) || !storedFileName.EndsWith(ExtensionFor(contentType), StringComparison.Ordinal))
            throw new ArgumentException("The stored file name is invalid for this content type.", nameof(storedFileName));
        if (!Sha256Pattern.IsMatch(sha256 ?? string.Empty))
            throw new ArgumentException("The SHA-256 digest must be 64 lowercase hexadecimal characters.", nameof(sha256));

        var trimmedDescription = MedicalRecord.Normalize(description);
        if (trimmedDescription is not null && trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException($"The description cannot exceed {MaxDescriptionLength} characters.", nameof(description));

        return new MedicalAttachment
        {
            Id = Guid.NewGuid(),
            MedicalRecordId = medicalRecordId,
            OriginalFileName = SanitizeOriginalFileName(originalFileName),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StoredFileName = storedFileName,
            Sha256 = sha256,
            Description = trimmedDescription,
            UploadedByDoctorId = uploadedByDoctorId,
            UploadedAtUtc = uploadedAtUtc.ToUniversalTime()
        };
    }

    public static bool IsSupportedContentType(string? contentType) =>
        contentType is ContentTypePdf or ContentTypeJpeg or ContentTypePng;

    public static string ExtensionFor(string contentType) => contentType switch
    {
        ContentTypePdf => ".pdf",
        ContentTypeJpeg => ".jpg",
        ContentTypePng => ".png",
        _ => throw new ArgumentException("Unsupported content type.", nameof(contentType))
    };

    /// <summary>A new server-generated name: 32 hex characters, a dot, and an extension derived from the detected type.</summary>
    public static string CreateStoredFileName(string contentType) =>
        Guid.NewGuid().ToString("N") + ExtensionFor(contentType);

    /// <summary>Strict shape check used before any path is built from a stored name.</summary>
    public static bool IsValidStoredFileName(string? storedFileName) =>
        storedFileName is not null && StoredFileNamePattern.IsMatch(storedFileName);

    /// <summary>
    /// Keeps only the last path segment, drops control characters and trims the result. The value is for display and
    /// download headers only; it is never used on disk.
    /// </summary>
    public static string SanitizeOriginalFileName(string? raw)
    {
        var value = raw ?? string.Empty;
        var lastSeparator = value.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
            value = value[(lastSeparator + 1)..];

        var cleaned = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length == 0 || cleaned is "." or "..")
            return "attachment";
        return cleaned.Length > MaxOriginalFileNameLength ? cleaned[..MaxOriginalFileNameLength] : cleaned;
    }
}
