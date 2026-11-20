using MediCare.Application.DTOs.MedicalRecords;
using MediCare.Domain.Entities;

namespace MediCare.Application.Mappings;

internal static class MedicalRecordMappings
{
    public static MedicalRecordDto ToDto(this MedicalRecord record, string doctorName) => new()
    {
        Id = record.Id,
        PatientProfileId = record.PatientProfileId,
        DoctorProfileId = record.DoctorProfileId,
        DoctorName = doctorName,
        AppointmentId = record.AppointmentId,
        Symptoms = record.Symptoms,
        Diagnosis = record.Diagnosis,
        TreatmentPlan = record.TreatmentPlan,
        DoctorNotes = record.DoctorNotes,
        CreatedAtUtc = record.CreatedAtUtc,
        UpdatedAtUtc = record.UpdatedAtUtc
    };
}
