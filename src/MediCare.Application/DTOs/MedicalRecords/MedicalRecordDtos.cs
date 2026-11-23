namespace MediCare.Application.DTOs.MedicalRecords;

public sealed class MedicalRecordDto
{
    public Guid Id { get; init; }

    public Guid PatientProfileId { get; init; }

    public Guid DoctorProfileId { get; init; }

    public string DoctorName { get; init; } = string.Empty;

    public Guid? AppointmentId { get; init; }

    public string Symptoms { get; init; } = string.Empty;

    public string Diagnosis { get; init; } = string.Empty;

    public string? TreatmentPlan { get; init; }

    public string? DoctorNotes { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class DoctorPatientDto
{
    public Guid PatientProfileId { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;
}

public sealed class CreateMedicalRecordRequest
{
    public Guid PatientProfileId { get; set; }

    public Guid? AppointmentId { get; set; }

    public string Symptoms { get; set; } = string.Empty;

    public string Diagnosis { get; set; } = string.Empty;

    public string? TreatmentPlan { get; set; }

    public string? DoctorNotes { get; set; }
}

public sealed class UpdateMedicalRecordRequest
{
    public Guid RecordId { get; set; }

    public string Symptoms { get; set; } = string.Empty;

    public string Diagnosis { get; set; } = string.Empty;

    public string? TreatmentPlan { get; set; }

    public string? DoctorNotes { get; set; }
}
