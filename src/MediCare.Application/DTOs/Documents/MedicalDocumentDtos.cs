namespace MediCare.Application.DTOs.Documents;

/// <summary>
/// The upload rules in one place: allowed types, the size limit and the file signatures
/// used to make sure a file really is what its extension claims.
/// </summary>
public static class MedicalDocumentRules
{
    public const long MaxSizeInBytes = 5 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg"
    };

    public static bool IsAllowedExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && ContentTypes.ContainsKey(extension);

    public static string? ContentTypeFor(string extension) =>
        ContentTypes.TryGetValue(extension, out var contentType) ? contentType : null;

    public static bool HasValidSignature(string extension, byte[] content) =>
        extension.ToLowerInvariant() switch
        {
            ".pdf" => StartsWith(content, 0x25, 0x50, 0x44, 0x46),
            ".png" => StartsWith(content, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".jpg" or ".jpeg" => StartsWith(content, 0xFF, 0xD8, 0xFF),
            _ => false
        };

    private static bool StartsWith(byte[] content, params byte[] prefix) =>
        content.Length >= prefix.Length && content.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}

public sealed class MedicalDocumentDto
{
    public Guid Id { get; init; }

    public Guid PatientProfileId { get; init; }

    public Guid? MedicalRecordId { get; init; }

    public string OriginalFileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long SizeInBytes { get; init; }

    public DateTime UploadedAtUtc { get; init; }

    // True when the person asking uploaded this document and may delete it.
    public bool CanDelete { get; init; }
}

public sealed class MedicalDocumentFileDto
{
    public Stream Content { get; init; } = Stream.Null;

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;
}

public sealed class UploadMedicalDocumentRequest
{
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeInBytes { get; set; }

    public Stream Content { get; set; } = Stream.Null;
}
