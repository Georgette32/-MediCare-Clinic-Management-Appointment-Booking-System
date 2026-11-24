using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

/// <summary>
/// Decides whether uploaded bytes may be attached: non-empty, within the configured size, and starting with a known
/// magic number. Client file names and declared Content-Type values are not part of the decision.
/// </summary>
public static class MedicalFileValidator
{
    public const int MagicHeaderLength = 8;

    public static MedicalFileValidation Validate(ReadOnlySpan<byte> content, long maxFileSizeBytes)
    {
        if (content.IsEmpty)
            return MedicalFileValidation.Rejected("The file is empty.");
        if (content.Length > maxFileSizeBytes)
            return MedicalFileValidation.Rejected($"The file is larger than the {FormatLimit(maxFileSizeBytes)} limit.");

        var contentType = MedicalFileSignature.Detect(content[..Math.Min(MagicHeaderLength, content.Length)]);
        return contentType is null
            ? MedicalFileValidation.Rejected("Only PDF, JPEG and PNG files whose content matches their type can be attached.")
            : MedicalFileValidation.Accepted(contentType);
    }

    private static string FormatLimit(long bytes) =>
        bytes % (1024 * 1024) == 0 ? $"{bytes / (1024 * 1024)} MB" : $"{bytes} bytes";
}

public sealed record MedicalFileValidation(bool IsValid, string? ContentType, string? Error)
{
    public static MedicalFileValidation Accepted(string contentType) => new(true, contentType, null);

    public static MedicalFileValidation Rejected(string error) => new(false, null, error);
}
