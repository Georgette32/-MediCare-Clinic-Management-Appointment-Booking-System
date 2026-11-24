using System.ComponentModel.DataAnnotations;
using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Web.Models;

public sealed class VisitRecordForm
{
    [StringLength(MedicalRecord.MaxChiefComplaintLength)]
    [Display(Name = "Chief complaint")]
    public string? ChiefComplaint { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(MedicalRecord.MaxDiagnosisLength)]
    [Display(Name = "Diagnosis")]
    public string Diagnosis { get; set; } = string.Empty;

    [StringLength(MedicalRecord.MaxClinicalNotesLength)]
    [Display(Name = "Clinical notes")]
    public string? ClinicalNotes { get; set; }
}

public sealed class AddendumForm
{
    /// <summary>Only used to return to the visit page after the post. Ownership is checked from the record id.</summary>
    public Guid QueueEntryId { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(MedicalRecordAddendum.MaxTextLength)]
    public string Text { get; set; } = string.Empty;
}

public sealed class PrescriptionForm
{
    /// <summary>Only used to return to the visit page after the post. Ownership is checked from the record id.</summary>
    public Guid QueueEntryId { get; set; }

    [StringLength(Prescription.MaxNotesLength)]
    public string? Notes { get; set; }

    public List<PrescriptionRowForm> Items { get; set; } = [];
}

public sealed class PrescriptionRowForm
{
    [StringLength(PrescriptionItem.MaxMedicationNameLength)]
    public string? MedicationName { get; set; }

    [StringLength(PrescriptionItem.MaxDosageLength)]
    public string? Dosage { get; set; }

    [StringLength(PrescriptionItem.MaxFrequencyLength)]
    public string? Frequency { get; set; }

    [Range(1, 3650)]
    public int? DurationDays { get; set; }

    [StringLength(PrescriptionItem.MaxInstructionsLength)]
    public string? Instructions { get; set; }
}

public sealed class VisitPageModel
{
    public required VisitPageView Page { get; init; }
    public required VisitRecordForm Visit { get; init; }
    public required PrescriptionForm Prescription { get; init; }
    /// <summary>Blank prescription rows shown after the existing items.</summary>
    public int BlankRows { get; init; }
}
