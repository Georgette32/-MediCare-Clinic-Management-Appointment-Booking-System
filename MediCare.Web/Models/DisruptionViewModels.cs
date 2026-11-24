using System.ComponentModel.DataAnnotations;
using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Web.Models;

public sealed class DisruptionsPageViewModel
{
    public IReadOnlyList<DoctorChoice> Doctors { get; init; } = [];
    public IReadOnlyList<DisruptionListItem> Recent { get; init; } = [];
    public DateOnly Today { get; init; }
}

public sealed class DisruptionRecordModel
{
    [Required(ErrorMessage = "Choose a doctor.")]
    public Guid? DoctorId { get; set; }

    [Required(ErrorMessage = "Enter the date.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Enter the time the doctor leaves.")]
    public TimeOnly? FromTime { get; set; }

    [Required(ErrorMessage = "A reason is required.")]
    [StringLength(DoctorDisruption.MaxReasonLength, ErrorMessage = "The reason cannot exceed 500 characters.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class DisruptionDetailPageViewModel
{
    public DisruptionWorklist Worklist { get; init; } = null!;
    public DateOnly Today { get; init; }
    public IReadOnlyList<DoctorChoice> Doctors { get; init; } = [];
    public Guid? RescheduleItemId { get; init; }
    public Guid? RescheduleDoctorId { get; init; }
    public DateOnly? RescheduleDate { get; init; }
    public IReadOnlyList<TimeOnly> AvailableSlots { get; init; } = [];

    public bool DayEndAvailable => !Worklist.DayEndRecorded && Worklist.Date <= Today;
}
