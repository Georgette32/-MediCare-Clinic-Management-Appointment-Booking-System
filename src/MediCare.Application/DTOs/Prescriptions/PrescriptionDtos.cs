namespace MediCare.Application.DTOs.Prescriptions;

public sealed class PrescriptionDto
{
    public Guid Id { get; init; }

    public Guid PatientProfileId { get; init; }

    public Guid DoctorProfileId { get; init; }

    public string DoctorName { get; init; } = string.Empty;

    public Guid? MedicalRecordId { get; init; }

    public DateTime IssuedAtUtc { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<PrescriptionItemDto> Items { get; init; } = Array.Empty<PrescriptionItemDto>();
}

public sealed class PrescriptionItemDto
{
    public Guid Id { get; init; }

    public string MedicationName { get; init; } = string.Empty;

    public string Dosage { get; init; } = string.Empty;

    public string Frequency { get; init; } = string.Empty;

    public int DurationInDays { get; init; }

    public string? Instructions { get; init; }
}

public sealed class CreatePrescriptionRequest
{
    public Guid PatientProfileId { get; set; }

    public Guid? MedicalRecordId { get; set; }

    public string? Notes { get; set; }

    public List<PrescriptionItemRequest> Items { get; set; } = new();
}

public sealed class PrescriptionItemRequest
{
    public string MedicationName { get; set; } = string.Empty;

    public string Dosage { get; set; } = string.Empty;

    public string Frequency { get; set; } = string.Empty;

    public int DurationInDays { get; set; }

    public string? Instructions { get; set; }
}
