using System.ComponentModel.DataAnnotations;
using MediCare.Application.DTOs.MedicalRecords;

namespace MediCare.Web.ViewModels.Doctor;

public sealed class MedicalRecordFormViewModel
{
    public Guid? RecordId { get; set; }

    public Guid PatientProfileId { get; set; }

    public Guid? AppointmentId { get; set; }

    [Required, StringLength(2000)]
    [Display(Name = "Symptoms")]
    public string Symptoms { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    [Display(Name = "Diagnosis")]
    public string Diagnosis { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Treatment plan")]
    public string? TreatmentPlan { get; set; }

    [StringLength(2000)]
    [Display(Name = "Doctor notes")]
    public string? DoctorNotes { get; set; }

    public bool IsEdit => RecordId.HasValue;
}

public sealed class DoctorPatientRecordsViewModel
{
    public Guid PatientProfileId { get; set; }

    public string PatientName { get; set; } = string.Empty;

    public IReadOnlyList<MedicalRecordDto> Records { get; set; } = Array.Empty<MedicalRecordDto>();
}
