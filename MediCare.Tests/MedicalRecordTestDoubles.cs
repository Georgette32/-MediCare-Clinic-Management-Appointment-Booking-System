using System.Reflection;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Tests;

/// <summary>Sets private navigation properties the way EF Core would on load.</summary>
internal static class MedicalNav
{
    public static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
}

/// <summary>
/// In-memory medical repository. Reads hydrate the navigation properties (patient, doctor, specialization, addendum
/// authors) from the other fakes, as an EF load would.
/// </summary>
internal sealed class InMemoryMedicalRecordRepository(InMemoryAppointmentRepository appointments, InMemoryQueueRepository queue) : IMedicalRecordRepository
{
    public Dictionary<Guid, Patient> Patients { get; } = [];
    public Dictionary<Guid, MedicalRecord> Records { get; } = [];

    public void AddPatient(Patient patient) => Patients[patient.Id] = patient;

    public Task<Guid?> FindPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Patients.Values.Where(patient => patient.ApplicationUserId == applicationUserId).Select(patient => (Guid?)patient.Id).SingleOrDefault());

    public Task<QueueEntry?> GetQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken) =>
        Task.FromResult(queue.Entries.GetValueOrDefault(queueEntryId));

    public Task<MedicalRecord?> GetRecordByQueueEntryIdAsync(Guid queueEntryId, CancellationToken cancellationToken) =>
        Task.FromResult(Hydrate(Records.Values.SingleOrDefault(record => record.QueueEntryId == queueEntryId)));

    public Task<MedicalRecord?> GetRecordByIdAsync(Guid recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Hydrate(Records.GetValueOrDefault(recordId)));

    public Task<IReadOnlyList<MedicalRecord>> GetRecordsForPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MedicalRecord>>(Records.Values
            .Where(record => record.PatientId == patientId)
            .OrderByDescending(record => record.VisitDate)
            .ThenByDescending(record => record.CreatedAtUtc)
            .Select(record => Hydrate(record)!)
            .ToArray());

    public Task<string?> GetPatientNameAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult(Patients.GetValueOrDefault(patientId)?.FullName);

    public Task<bool> HasTreatingRelationshipAsync(Guid doctorId, Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult(
            queue.Entries.Values.Any(entry => entry.DoctorId == doctorId && entry.PatientId == patientId &&
                (entry.Status == MediCare.Domain.Enums.QueueEntryStatus.InConsultation || entry.Status == MediCare.Domain.Enums.QueueEntryStatus.Completed)) ||
            appointments.Appointments.Values.Any(appointment => appointment.DoctorId == doctorId && appointment.PatientId == patientId &&
                (appointment.Status == MediCare.Domain.Enums.AppointmentStatus.Confirmed || appointment.Status == MediCare.Domain.Enums.AppointmentStatus.Completed)));

    public Task<IReadOnlyList<DoctorVisitSummary>> GetDoctorVisitsForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorVisitSummary>>(queue.Entries.Values
            .Where(entry => entry.DoctorId == doctorId && entry.QueueDate == date &&
                (entry.Status == MediCare.Domain.Enums.QueueEntryStatus.InConsultation || entry.Status == MediCare.Domain.Enums.QueueEntryStatus.Completed))
            .Select(entry => new DoctorVisitSummary(
                entry.Id,
                entry.PatientId,
                Patients[entry.PatientId].FullName,
                entry.Status,
                Records.Values.SingleOrDefault(record => record.QueueEntryId == entry.Id)?.Id))
            .ToArray());

    public Task AddRecordAsync(MedicalRecord record, CancellationToken cancellationToken)
    {
        Hydrate(record);
        Records[record.Id] = record;
        return Task.CompletedTask;
    }

    private MedicalRecord? Hydrate(MedicalRecord? record)
    {
        if (record is null)
            return null;

        var patient = Patients[record.PatientId];
        var doctor = appointments.Doctors[record.DoctorId];
        MedicalNav.Set(record, nameof(MedicalRecord.Patient), patient);
        MedicalNav.Set(record, nameof(MedicalRecord.Doctor), doctor);
        foreach (var addendum in record.Addenda)
        {
            if (addendum.Doctor is null)
                MedicalNav.Set(addendum, nameof(MedicalRecordAddendum.Doctor), appointments.Doctors[addendum.DoctorId]);
        }
        foreach (var attachment in record.Attachments)
        {
            if (attachment.UploadedByDoctor is null)
                MedicalNav.Set(attachment, nameof(MedicalAttachment.UploadedByDoctor), appointments.Doctors[attachment.UploadedByDoctorId]);
        }
        return record;
    }
}
