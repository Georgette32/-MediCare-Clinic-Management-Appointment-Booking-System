using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class MedicalRecordService(
    IMedicalRecordRepository records,
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : IMedicalRecordService
{
    private const string NotFoundMessage = "The record was not found or is not available to you.";

    public async Task<MedicalActionResult> CreateOrUpdateForVisitAsync(Guid queueEntryId, Guid doctorUserId, VisitRecordInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return Fail("Your account is not linked to an active doctor profile.");

        try
        {
            return await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
            {
                var entry = await records.GetQueueEntryAsync(queueEntryId, ct);
                if (entry is null || entry.DoctorId != doctor.Id)
                    return Fail("The visit was not found in your queue.");

                var now = clinicClock.UtcNow;
                var existing = await records.GetRecordByQueueEntryIdAsync(queueEntryId, ct);
                if (existing is null)
                {
                    var created = MedicalRecord.CreateForVisit(entry, doctor.Id, input.ChiefComplaint, input.Diagnosis, input.ClinicalNotes, now);
                    await records.AddRecordAsync(created, ct);
                    return Ok("Visit record saved.", created.Id);
                }

                existing.UpdateVisit(doctor.Id, clinicClock.Today, input.ChiefComplaint, input.Diagnosis, input.ClinicalNotes, now);
                return Ok("Visit record updated.", existing.Id);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
        catch (UniqueConstraintConflictException)
        {
            return Fail("A visit record for this attendance already exists. Reload the page.");
        }
    }

    public async Task<MedicalActionResult> AddAddendumAsync(Guid recordId, Guid doctorUserId, string text, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return Fail("Your account is not linked to an active doctor profile.");

        try
        {
            return await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
            {
                var record = await records.GetRecordByIdAsync(recordId, ct);
                if (record is null)
                    return Fail(NotFoundMessage);

                var viewer = new MedicalViewer(MedicalViewerRole.Doctor, null, doctor.Id);
                var related = await records.HasTreatingRelationshipAsync(doctor.Id, record.PatientId, ct);
                if (!MedicalRecordAccessPolicy.CanAddAddendum(viewer, related))
                    return Fail(NotFoundMessage);

                record.AddAddendum(doctor.Id, clinicClock.Today, text, clinicClock.UtcNow);
                return Ok("Addendum added.", record.Id);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public async Task<MedicalActionResult> SavePrescriptionAsync(Guid recordId, Guid doctorUserId, IReadOnlyList<PrescriptionItemInput> items, string? notes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return Fail("Your account is not linked to an active doctor profile.");

        var drafts = items
            .Where(item => !IsBlankRow(item))
            .Select(item => new PrescriptionItemDraft(item.MedicationName, item.Dosage, item.Frequency, item.DurationDays ?? 0, item.Instructions))
            .ToArray();
        if (drafts.Length == 0)
            return Fail("Add at least one prescription item.");

        try
        {
            return await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
            {
                var record = await records.GetRecordByIdAsync(recordId, ct);
                if (record is null)
                    return Fail(NotFoundMessage);

                var viewer = new MedicalViewer(MedicalViewerRole.Doctor, null, doctor.Id);
                if (!MedicalRecordAccessPolicy.CanEditVisit(viewer, record, clinicClock.Today))
                    return Fail("The prescription can be changed only by the doctor who created the visit, on the visit day.");

                record.SetPrescription(doctor.Id, clinicClock.Today, drafts, notes, clinicClock.UtcNow);
                return Ok("Prescription saved.", record.Id);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException)
        {
            return Fail(exception.Message);
        }
    }

    public async Task<VisitPageView?> GetVisitForDoctorAsync(Guid queueEntryId, Guid doctorUserId, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return null;

        var entry = await records.GetQueueEntryAsync(queueEntryId, cancellationToken);
        if (entry is null || entry.DoctorId != doctor.Id)
            return null;

        var today = clinicClock.Today;
        var viewer = new MedicalViewer(MedicalViewerRole.Doctor, null, doctor.Id);
        var record = await records.GetRecordByQueueEntryIdAsync(queueEntryId, cancellationToken);
        var recordView = record is null
            ? null
            : await BuildRecordViewAsync(record, viewer, today, cancellationToken);

        var visitable = entry.Status is QueueEntryStatus.InConsultation or QueueEntryStatus.Completed;
        return new VisitPageView(
            entry.Id,
            entry.PatientId,
            entry.Patient.FullName,
            entry.Status,
            entry.QueueDate,
            recordView,
            CanCreate: visitable && record is null,
            CanEdit: record is not null && MedicalRecordAccessPolicy.CanEditVisit(viewer, record, today));
    }

    public async Task<MedicalRecordView?> GetRecordAsync(Guid recordId, Guid viewerUserId, MedicalViewerRole role, CancellationToken cancellationToken)
    {
        var viewer = await ResolveViewerAsync(viewerUserId, role, cancellationToken);
        var record = await records.GetRecordByIdAsync(recordId, cancellationToken);
        if (record is null)
            return null;

        var related = await RelationshipAsync(viewer, record.PatientId, cancellationToken);
        if (!MedicalRecordAccessPolicy.CanViewPatientHistory(viewer, record.PatientId, related))
            return null;

        return await BuildRecordViewAsync(record, viewer, clinicClock.Today, cancellationToken);
    }

    public async Task<PatientHistoryView?> GetPatientHistoryAsync(Guid patientId, Guid viewerUserId, MedicalViewerRole role, CancellationToken cancellationToken)
    {
        var viewer = await ResolveViewerAsync(viewerUserId, role, cancellationToken);
        var related = await RelationshipAsync(viewer, patientId, cancellationToken);
        if (!MedicalRecordAccessPolicy.CanViewPatientHistory(viewer, patientId, related))
            return null;

        var name = await records.GetPatientNameAsync(patientId, cancellationToken);
        if (name is null)
            return null;

        var today = clinicClock.Today;
        var visits = new List<MedicalRecordView>();
        foreach (var record in await records.GetRecordsForPatientAsync(patientId, cancellationToken))
            visits.Add(await BuildRecordViewAsync(record, viewer, today, cancellationToken));

        return new PatientHistoryView(patientId, name, visits);
    }

    public async Task<PatientHistoryView?> GetMyHistoryAsync(Guid patientUserId, CancellationToken cancellationToken)
    {
        var patientId = await records.FindPatientIdForUserAsync(patientUserId, cancellationToken);
        if (patientId is null)
            return null;

        return await GetPatientHistoryAsync(patientId.Value, patientUserId, MedicalViewerRole.Patient, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivePrescriptionView>> GetActivePrescriptionsAsync(Guid patientUserId, CancellationToken cancellationToken)
    {
        var patientId = await records.FindPatientIdForUserAsync(patientUserId, cancellationToken);
        if (patientId is null)
            return [];

        var today = clinicClock.Today;
        var active = new List<ActivePrescriptionView>();
        foreach (var record in await records.GetRecordsForPatientAsync(patientId.Value, cancellationToken))
        {
            if (record.Prescription is null)
                continue;

            foreach (var item in record.Prescription.ActiveItemsOn(record.VisitDate, today))
            {
                active.Add(new ActivePrescriptionView(
                    record.Id,
                    record.VisitDate,
                    record.Doctor.FullName,
                    record.Doctor.Specialization.Name,
                    item.MedicationName,
                    item.Dosage,
                    item.Frequency,
                    item.DurationDays,
                    item.Instructions,
                    record.VisitDate.AddDays(item.DurationDays - 1)));
            }
        }

        return active;
    }

    public async Task<IReadOnlyList<DoctorVisitSummary>> GetDoctorVisitsForDateAsync(Guid doctorUserId, DateOnly date, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return [];

        return await records.GetDoctorVisitsForDateAsync(doctor.Id, date, cancellationToken);
    }

    private async Task<MedicalViewer> ResolveViewerAsync(Guid userId, MedicalViewerRole role, CancellationToken cancellationToken) => role switch
    {
        MedicalViewerRole.Patient => new MedicalViewer(role, await records.FindPatientIdForUserAsync(userId, cancellationToken), null),
        MedicalViewerRole.Doctor => new MedicalViewer(role, null, (await appointments.GetDoctorByApplicationUserIdAsync(userId, cancellationToken))?.Id),
        _ => new MedicalViewer(role, null, null)
    };

    private async Task<bool> RelationshipAsync(MedicalViewer viewer, Guid patientId, CancellationToken cancellationToken) =>
        viewer.Role == MedicalViewerRole.Doctor && viewer.DoctorId is Guid doctorId &&
        await records.HasTreatingRelationshipAsync(doctorId, patientId, cancellationToken);

    private async Task<MedicalRecordView> BuildRecordViewAsync(MedicalRecord record, MedicalViewer viewer, DateOnly today, CancellationToken cancellationToken)
    {
        var related = await RelationshipAsync(viewer, record.PatientId, cancellationToken);
        var prescription = record.Prescription is null
            ? null
            : new PrescriptionView(
                record.Prescription.IssuedAtUtc,
                record.Prescription.Notes,
                record.Prescription.Items
                    .Select(item => new PrescriptionItemView(
                        item.MedicationName,
                        item.Dosage,
                        item.Frequency,
                        item.DurationDays,
                        item.Instructions,
                        item.IsActiveOn(record.VisitDate, today),
                        record.VisitDate.AddDays(item.DurationDays - 1)))
                    .ToArray());

        return new MedicalRecordView(
            record.Id,
            record.PatientId,
            record.Patient.FullName,
            record.VisitDate,
            record.Doctor.FullName,
            record.Doctor.Specialization.Name,
            record.ChiefComplaint,
            record.Diagnosis,
            record.ClinicalNotes,
            CanEdit: MedicalRecordAccessPolicy.CanEditVisit(viewer, record, today),
            CanAddAddendum: MedicalRecordAccessPolicy.CanAddAddendum(viewer, related) && record.VisitDate < today,
            prescription,
            record.Addenda
                .OrderBy(addendum => addendum.CreatedAtUtc)
                .Select(addendum => new AddendumView(addendum.CreatedAtUtc, addendum.Doctor.FullName, addendum.Text))
                .ToArray(),
            record.Attachments
                .OrderBy(attachment => attachment.UploadedAtUtc)
                .Select(attachment => new MedicalAttachmentView(
                    attachment.Id,
                    attachment.OriginalFileName,
                    attachment.ContentType,
                    attachment.SizeBytes,
                    attachment.Description,
                    attachment.UploadedByDoctor.FullName,
                    attachment.UploadedAtUtc))
                .ToArray(),
            CanUploadAttachments: MedicalRecordAccessPolicy.CanUploadAttachment(viewer, related));
    }

    private static bool IsBlankRow(PrescriptionItemInput item) =>
        string.IsNullOrWhiteSpace(item.MedicationName) &&
        string.IsNullOrWhiteSpace(item.Dosage) &&
        string.IsNullOrWhiteSpace(item.Frequency) &&
        string.IsNullOrWhiteSpace(item.Instructions) &&
        item.DurationDays is null;

    private static MedicalActionResult Ok(string message, Guid recordId) => new(true, message, recordId);

    private static MedicalActionResult Fail(string message) => new(false, message);
}
