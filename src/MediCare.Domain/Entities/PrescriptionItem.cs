using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

public sealed class PrescriptionItem : BaseEntity
{
    private PrescriptionItem()
    {
        // Required by EF Core.
    }

    // Internal: items are created only through Prescription.AddItem(...).
    internal PrescriptionItem(
        Guid prescriptionId,
        string medicationName,
        string dosage,
        string frequency,
        int durationInDays,
        string? instructions)
    {
        if (prescriptionId == Guid.Empty)
        {
            throw new DomainException("A prescription item must belong to a prescription.");
        }

        if (string.IsNullOrWhiteSpace(medicationName))
        {
            throw new DomainException("The medication name is required.");
        }

        if (string.IsNullOrWhiteSpace(dosage))
        {
            throw new DomainException("The dosage is required.");
        }

        if (string.IsNullOrWhiteSpace(frequency))
        {
            throw new DomainException("The frequency is required.");
        }

        if (durationInDays <= 0)
        {
            throw new DomainException("The duration must be at least one day.");
        }

        PrescriptionId = prescriptionId;
        MedicationName = medicationName.Trim();
        Dosage = dosage.Trim();
        Frequency = frequency.Trim();
        DurationInDays = durationInDays;
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
    }

    public Guid PrescriptionId { get; private set; }

    public string MedicationName { get; private set; } = string.Empty;

    // Example: "500 mg".
    public string Dosage { get; private set; } = string.Empty;

    // Example: "3 times daily".
    public string Frequency { get; private set; } = string.Empty;

    public int DurationInDays { get; private set; }

    // Example: "after meals".
    public string? Instructions { get; private set; }
}
