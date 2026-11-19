using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

public sealed class Prescription : BaseEntity
{
    private readonly List<PrescriptionItem> _items = new();

    private Prescription()
    {
        // Required by EF Core.
    }

    public Prescription(
        Guid patientProfileId,
        Guid doctorProfileId,
        Guid? medicalRecordId,
        string? notes)
    {
        if (patientProfileId == Guid.Empty)
        {
            throw new DomainException("A prescription must belong to a patient.");
        }

        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("A prescription must be written by a doctor.");
        }

        if (medicalRecordId == Guid.Empty)
        {
            throw new DomainException("The medical record id is not valid.");
        }

        PatientProfileId = patientProfileId;
        DoctorProfileId = doctorProfileId;
        MedicalRecordId = medicalRecordId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public Guid PatientProfileId { get; private set; }

    public Guid DoctorProfileId { get; private set; }

    public Guid? MedicalRecordId { get; private set; }

    public DateTime IssuedAtUtc { get; private set; } = DateTime.UtcNow;

    public string? Notes { get; private set; }

    public bool IsDeleted { get; private set; }

    public IReadOnlyCollection<PrescriptionItem> Items => _items.AsReadOnly();

    public void AddItem(
        string medicationName,
        string dosage,
        string frequency,
        int durationInDays,
        string? instructions)
    {
        if (IsDeleted)
        {
            throw new DomainException("A deleted prescription cannot be changed.");
        }

        _items.Add(new PrescriptionItem(
            Id,
            medicationName,
            dosage,
            frequency,
            durationInDays,
            instructions));

        MarkUpdated();
    }

    public void Delete()
    {
        IsDeleted = true;
        MarkUpdated();
    }
}
