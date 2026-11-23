using System.ComponentModel.DataAnnotations;
using MediCare.Application.DTOs.Prescriptions;

namespace MediCare.Web.ViewModels.Doctor;

public sealed class PrescriptionFormViewModel
{
    public Guid PatientProfileId { get; set; }

    public Guid? MedicalRecordId { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public List<PrescriptionItemInput> Items { get; set; } = new();
}

public sealed class PrescriptionItemInput
{
    [StringLength(200)]
    public string? MedicationName { get; set; }

    [StringLength(100)]
    public string? Dosage { get; set; }

    [StringLength(100)]
    public string? Frequency { get; set; }

    [Range(1, 365)]
    public int? DurationInDays { get; set; }

    [StringLength(500)]
    public string? Instructions { get; set; }
}

public sealed class DoctorPatientPrescriptionsViewModel
{
    public Guid PatientProfileId { get; set; }

    public string PatientName { get; set; } = string.Empty;

    public IReadOnlyList<PrescriptionDto> Prescriptions { get; set; } = Array.Empty<PrescriptionDto>();
}
