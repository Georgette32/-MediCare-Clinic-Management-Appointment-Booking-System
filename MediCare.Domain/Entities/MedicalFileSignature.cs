namespace MediCare.Domain.Entities;

/// <summary>
/// Recognises the allowed attachment types from their leading bytes (magic numbers). The client file name and the
/// declared Content-Type are never trusted for this decision.
/// </summary>
public static class MedicalFileSignature
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns the content type for a recognised header, or null when the bytes match no allowed type.</summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PdfSignature))
            return MedicalAttachment.ContentTypePdf;
        if (header.StartsWith(JpegSignature))
            return MedicalAttachment.ContentTypeJpeg;
        if (header.StartsWith(PngSignature))
            return MedicalAttachment.ContentTypePng;
        return null;
    }
}
