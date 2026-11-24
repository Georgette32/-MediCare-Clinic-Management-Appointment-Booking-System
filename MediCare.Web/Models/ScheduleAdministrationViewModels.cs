using System.ComponentModel.DataAnnotations;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

public sealed class SpecializationFormModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "A specialization name is required.")]
    [StringLength(120, ErrorMessage = "The name cannot exceed 120 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "The description cannot exceed 2000 characters.")]
    public string? Description { get; set; }
}

public sealed class SpecializationsPageModel
{
    public IReadOnlyList<SpecializationAdminRow> Items { get; set; } = [];
}

public sealed class ClinicSettingsFormModel
{
    [Range(1, 100000, ErrorMessage = "The minimum sample size must be a positive whole number. Leave it empty to clear it.")]
    public int? MinimumConsultationSampleSize { get; set; }
}

public sealed class ClinicSettingsPageModel
{
    public ClinicSettingsView Settings { get; set; } = new(null, null, null);
    public string ClinicTimeZoneId { get; set; } = string.Empty;
    public int WalkInPendingDurationMinutes { get; set; }
}

public sealed class AdminDoctorSchedulePageModel
{
    public IReadOnlyList<StaffAccountRow> Doctors { get; set; } = [];
    public DoctorScheduleView? Schedule { get; set; }
}

public sealed class DoctorSearchPageModel
{
    public IReadOnlyList<DoctorDirectoryItem> Doctors { get; set; } = [];
    public IReadOnlyList<SpecializationAdminRow> Specializations { get; set; } = [];
    public Guid? SpecializationId { get; set; }
    public string? Name { get; set; }
    public DateOnly? Date { get; set; }
    public DateOnly Today { get; set; }
}
