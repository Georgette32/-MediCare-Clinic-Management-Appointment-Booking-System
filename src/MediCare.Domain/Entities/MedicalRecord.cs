using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

public sealed class MedicalRecord : BaseEntity
{
    private MedicalRecord()
    {
        // Required by EF Core.
    }

    public MedicalRecord(
        Guid patientProfileId,
        Guid doctorProfileId,
        Guid? appointmentId,
        string symptoms,
        string diagnosis,
        string? treatmentPlan,
        string? doctorNotes)
    {
        if (patientProfileId == Guid.Empty)
        {
            throw new DomainException("A medical record must belong to a patient.");
        }

        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("A medical record must be written by a doctor.");
        }

        if (appointmentId == Guid.Empty)
        {
            throw new DomainException("The appointment id is not valid.");
        }

        PatientProfileId = patientProfileId;
        DoctorProfileId = doctorProfileId;
        AppointmentId = appointmentId;

        SetClinicalDetails(symptoms, diagnosis, treatmentPlan, doctorNotes);
    }

    public Guid PatientProfileId { get; private set; }

    public Guid DoctorProfileId { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public string Symptoms { get; private set; } = string.Empty;

    public string Diagnosis { get; private set; } = string.Empty;

    public string? TreatmentPlan { get; private set; }

    public string? DoctorNotes { get; private set; }

    // Medical history must never be physically deleted.
    public bool IsDeleted { get; private set; }

    public void Update(
        string symptoms,
        string diagnosis,
        string? treatmentPlan,
        string? doctorNotes)
    {
        if (IsDeleted)
        {
            throw new DomainException("A deleted medical record cannot be changed.");
        }

        SetClinicalDetails(symptoms, diagnosis, treatmentPlan, doctorNotes);
        MarkUpdated();
    }

    public void Delete()
    {
        IsDeleted = true;
        MarkUpdated();
    }

    private void SetClinicalDetails(
        string symptoms,
        string diagnosis,
        string? treatmentPlan,
        string? doctorNotes)
    {
        if (string.IsNullOrWhiteSpace(symptoms))
        {
            throw new DomainException("Symptoms are required.");
        }

        if (string.IsNullOrWhiteSpace(diagnosis))
        {
            throw new DomainException("A diagnosis is required.");
        }

        Symptoms = symptoms.Trim();
        Diagnosis = diagnosis.Trim();
        TreatmentPlan = Normalize(treatmentPlan);
        DoctorNotes = Normalize(doctorNotes);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
