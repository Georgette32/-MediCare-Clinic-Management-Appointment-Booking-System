using System.ComponentModel.DataAnnotations;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

public class WorkingHourExtensionRequestModel
{
    [Required(ErrorMessage = "Choose a date.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Choose the window end time to extend.")]
    public TimeOnly? OriginalEndTime { get; set; }

    [Required(ErrorMessage = "Enter the new end time.")]
    public TimeOnly? ExtendedEndTime { get; set; }

    [Required(ErrorMessage = "A reason is required.")]
    [StringLength(500, ErrorMessage = "The reason cannot exceed 500 characters.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReceptionExtensionRequestModel : WorkingHourExtensionRequestModel
{
    [Required(ErrorMessage = "Choose a doctor.")]
    public Guid? DoctorId { get; set; }
}

public sealed class DoctorExtensionDecisionModel
{
    public Guid ExtensionId { get; set; }
    public bool Approve { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }
}

public sealed class DoctorExtensionsViewModel
{
    public DateOnly Today { get; set; }
    public IReadOnlyList<WorkingHourExtensionView> Own { get; set; } = [];
    public IReadOnlyList<WorkingHourExtensionView> Pending { get; set; } = [];
    public DateOnly? SelectedDate { get; set; }
    public IReadOnlyList<TimeOnly> WindowEnds { get; set; } = [];
    public WorkingHourExtensionRequestModel Form { get; set; } = new();
}

public sealed class ReceptionExtensionsViewModel
{
    public DateOnly Today { get; set; }
    public IReadOnlyList<DoctorOption> Doctors { get; set; } = [];
    public Guid? SelectedDoctorId { get; set; }
    public DateOnly? SelectedDate { get; set; }
    public IReadOnlyList<TimeOnly> WindowEnds { get; set; } = [];
    public IReadOnlyList<WorkingHourExtensionView> Extensions { get; set; } = [];
    public ReceptionExtensionRequestModel Form { get; set; } = new();
}
