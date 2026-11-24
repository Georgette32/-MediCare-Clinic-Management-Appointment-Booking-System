using System.ComponentModel.DataAnnotations;

namespace MediCare.Application.Common;

/// <summary>
/// Medical attachment storage settings (section "MedicalFiles"). StoragePath may be relative to the content root; the
/// Infrastructure layer resolves it and refuses any location inside the web root.
/// </summary>
public sealed class MedicalFileOptions
{
    public const string SectionName = "MedicalFiles";

    [Required(AllowEmptyStrings = false)]
    public string StoragePath { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; set; }
}
