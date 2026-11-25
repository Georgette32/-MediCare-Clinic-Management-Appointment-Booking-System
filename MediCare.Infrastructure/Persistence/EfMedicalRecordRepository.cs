using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfMedicalRecordRepository(MediCareDbContext dbContext) : IMedicalRecordRepository
{
    public Task<Guid?> FindPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        dbContext.Patients
            .Where(patient => patient.ApplicationUserId == applicationUserId)
            .Select(patient => (Guid?)patient.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<QueueEntry?> GetQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken) =>
        dbContext.QueueEntries
            .Include(entry => entry.Patient)
            .SingleOrDefaultAsync(entry => entry.Id == queueEntryId, cancellationToken);

    public Task<MedicalRecord?> GetRecordByQueueEntryIdAsync(Guid queueEntryId, CancellationToken cancellationToken) =>
        RecordQuery().SingleOrDefaultAsync(record => record.QueueEntryId == queueEntryId, cancellationToken);

    public Task<MedicalRecord?> GetRecordByIdAsync(Guid recordId, CancellationToken cancellationToken) =>
        RecordQuery().SingleOrDefaultAsync(record => record.Id == recordId, cancellationToken);

    public async Task<IReadOnlyList<MedicalRecord>> GetRecordsForPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        await RecordQuery()
            .Where(record => record.PatientId == patientId)
            .OrderByDescending(record => record.VisitDate)
            .ThenByDescending(record => record.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<string?> GetPatientNameAsync(Guid patientId, CancellationToken cancellationToken) =>
        dbContext.Patients
            .Where(patient => patient.Id == patientId)
            .Select(patient => patient.FullName)
            .SingleOrDefaultAsync(cancellationToken);

    // A patient currently Called to the doctor counts too, so the history is ready at "Start consultation"; the access
    // lapses again if that attempt ends as a no-show or is cancelled.
    public async Task<bool> HasTreatingRelationshipAsync(Guid doctorId, Guid patientId, CancellationToken cancellationToken)
    {
        if (await dbContext.QueueEntries.AnyAsync(entry =>
                entry.DoctorId == doctorId && entry.PatientId == patientId &&
                (entry.Status == QueueEntryStatus.Called || entry.Status == QueueEntryStatus.InConsultation || entry.Status == QueueEntryStatus.Completed),
            cancellationToken))
        {
            return true;
        }

        return await dbContext.Appointments.AnyAsync(appointment =>
                appointment.DoctorId == doctorId && appointment.PatientId == patientId &&
                (appointment.Status == AppointmentStatus.Confirmed || appointment.Status == AppointmentStatus.Completed),
            cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorVisitSummary>> GetDoctorVisitsForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        var entries = await dbContext.QueueEntries
            .Include(entry => entry.Patient)
            .Where(entry => entry.DoctorId == doctorId && entry.QueueDate == date &&
                (entry.Status == QueueEntryStatus.InConsultation || entry.Status == QueueEntryStatus.Completed))
            .OrderBy(entry => entry.ArrivedAt)
            .ToListAsync(cancellationToken);

        var recordByEntry = await dbContext.MedicalRecords
            .Where(record => record.DoctorId == doctorId && record.VisitDate == date)
            .Select(record => new { record.QueueEntryId, record.Id })
            .ToDictionaryAsync(item => item.QueueEntryId, item => item.Id, cancellationToken);

        return entries
            .Select(entry => new DoctorVisitSummary(
                entry.Id,
                entry.PatientId,
                entry.Patient.FullName,
                entry.Status,
                recordByEntry.TryGetValue(entry.Id, out var recordId) ? recordId : null))
            .ToArray();
    }

    public Task AddRecordAsync(MedicalRecord record, CancellationToken cancellationToken) =>
        dbContext.MedicalRecords.AddAsync(record, cancellationToken).AsTask();

    private IQueryable<MedicalRecord> RecordQuery() =>
        dbContext.MedicalRecords
            .Include(record => record.Patient)
            .Include(record => record.Doctor)
                .ThenInclude(doctor => doctor.Specialization)
            .Include(record => record.Prescription)
                .ThenInclude(prescription => prescription!.Items)
            .Include(record => record.Addenda)
                .ThenInclude(addendum => addendum.Doctor)
            .Include(record => record.Attachments)
                .ThenInclude(attachment => attachment.UploadedByDoctor);
}
