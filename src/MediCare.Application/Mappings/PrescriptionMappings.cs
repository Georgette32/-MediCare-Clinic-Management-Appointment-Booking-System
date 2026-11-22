using MediCare.Application.DTOs.Prescriptions;
using MediCare.Domain.Entities;

namespace MediCare.Application.Mappings;

internal static class PrescriptionMappings
{
    public static PrescriptionDto ToDto(this Prescription prescription, string doctorName) => new()
    {
        Id = prescription.Id,
        PatientProfileId = prescription.PatientProfileId,
        DoctorProfileId = prescription.DoctorProfileId,
        DoctorName = doctorName,
        MedicalRecordId = prescription.MedicalRecordId,
        IssuedAtUtc = prescription.IssuedAtUtc,
        Notes = prescription.Notes,
        Items = prescription.Items
            .Select(i => new PrescriptionItemDto
            {
                Id = i.Id,
                MedicationName = i.MedicationName,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationInDays = i.DurationInDays,
                Instructions = i.Instructions
            })
            .ToList()
    };
}
