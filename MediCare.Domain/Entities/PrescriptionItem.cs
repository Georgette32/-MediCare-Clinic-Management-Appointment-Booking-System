namespace MediCare.Domain.Entities;

public sealed class PrescriptionItem
{
    public const int MaxMedicationNameLength = 200;
    public const int MaxDosageLength = 100;
    public const int MaxFrequencyLength = 100;
    public const int MaxInstructionsLength = 500;

    private PrescriptionItem() { }

    public Guid Id { get; private set; }
    public Guid PrescriptionId { get; private set; }
    public string MedicationName { get; private set; } = string.Empty;
    public string Dosage { get; private set; } = string.Empty;
    public string Frequency { get; private set; } = string.Empty;
    public int DurationDays { get; private set; }
    public string? Instructions { get; private set; }

    internal static PrescriptionItem Create(Guid prescriptionId, PrescriptionItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = Required(draft.MedicationName, nameof(draft), MaxMedicationNameLength, "medication name");
        var dosage = Required(draft.Dosage, nameof(draft), MaxDosageLength, "dosage");
        var frequency = Required(draft.Frequency, nameof(draft), MaxFrequencyLength, "frequency");
        if (draft.DurationDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(draft), "The duration must be at least one day.");

        var instructions = MedicalRecord.Normalize(draft.Instructions);
        if (instructions is not null && instructions.Length > MaxInstructionsLength)
            throw new ArgumentException($"The instructions cannot exceed {MaxInstructionsLength} characters.", nameof(draft));

        return new PrescriptionItem
        {
            Id = Guid.NewGuid(),
            PrescriptionId = prescriptionId,
            MedicationName = name,
            Dosage = dosage,
            Frequency = frequency,
            DurationDays = draft.DurationDays,
            Instructions = instructions
        };
    }

    /// <summary>Active while the clinic date is within [issue date, issue date + DurationDays - 1].</summary>
    public bool IsActiveOn(DateOnly issuedClinicDate, DateOnly clinicToday) =>
        clinicToday >= issuedClinicDate && clinicToday <= issuedClinicDate.AddDays(DurationDays - 1);

    private static string Required(string? value, string parameter, int maxLength, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException($"The {label} is required.", parameter);
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"The {label} cannot exceed {maxLength} characters.", parameter);
        return trimmed;
    }
}

/// <summary>Input for one prescription item before validation. Built by the application layer from the submitted form.</summary>
public sealed record PrescriptionItemDraft(string? MedicationName, string? Dosage, string? Frequency, int DurationDays, string? Instructions);
