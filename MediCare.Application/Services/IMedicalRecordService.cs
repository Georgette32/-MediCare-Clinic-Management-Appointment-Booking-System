using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Visit records, addenda, prescriptions and their read models. Ownership is always resolved on the server from the
/// signed-in user (Doctor.ApplicationUserId / Patient.ApplicationUserId), never from submitted ids alone. Writes run in
/// serializable transactions. Read methods return null (not an exception) for records the viewer may not see.
/// </summary>
public interface IMedicalRecordService
{
    /// <summary>Creates the visit record for an In-Consultation or Completed attempt of the doctor, or updates it while editable.</summary>
    Task<MedicalActionResult> CreateOrUpdateForVisitAsync(Guid queueEntryId, Guid doctorUserId, VisitRecordInput input, CancellationToken cancellationToken);

    /// <summary>Adds an addendum after the visit day. Requires a treating relationship with the patient.</summary>
    Task<MedicalActionResult> AddAddendumAsync(Guid recordId, Guid doctorUserId, string text, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the prescription items while the record is editable by the authoring doctor. Blank rows are ignored.</summary>
    Task<MedicalActionResult> SavePrescriptionAsync(Guid recordId, Guid doctorUserId, IReadOnlyList<PrescriptionItemInput> items, string? notes, CancellationToken cancellationToken);

    /// <summary>The doctor's page for one attendance attempt, or null when the entry is not in the doctor's queue.</summary>
    Task<VisitPageView?> GetVisitForDoctorAsync(Guid queueEntryId, Guid doctorUserId, CancellationToken cancellationToken);

    Task<MedicalRecordView?> GetRecordAsync(Guid recordId, Guid viewerUserId, MedicalViewerRole role, CancellationToken cancellationToken);

    Task<PatientHistoryView?> GetPatientHistoryAsync(Guid patientId, Guid viewerUserId, MedicalViewerRole role, CancellationToken cancellationToken);

    Task<PatientHistoryView?> GetMyHistoryAsync(Guid patientUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivePrescriptionView>> GetActivePrescriptionsAsync(Guid patientUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DoctorVisitSummary>> GetDoctorVisitsForDateAsync(Guid doctorUserId, DateOnly date, CancellationToken cancellationToken);
}

public sealed record MedicalActionResult(bool Succeeded, string Message, Guid? RecordId = null);

public sealed record VisitRecordInput(string? ChiefComplaint, string Diagnosis, string? ClinicalNotes);

/// <summary>One submitted prescription row. A row with every field empty is ignored.</summary>
public sealed record PrescriptionItemInput(string? MedicationName, string? Dosage, string? Frequency, int? DurationDays, string? Instructions);

public sealed record DoctorVisitSummary(
    Guid QueueEntryId,
    Guid PatientId,
    string PatientName,
    QueueEntryStatus Status,
    Guid? RecordId);

public sealed record VisitPageView(
    Guid QueueEntryId,
    Guid PatientId,
    string PatientName,
    QueueEntryStatus Status,
    DateOnly VisitDate,
    MedicalRecordView? Record,
    bool CanCreate,
    bool CanEdit);

public sealed record MedicalRecordView(
    Guid RecordId,
    Guid PatientId,
    string PatientName,
    DateOnly VisitDate,
    string DoctorName,
    string Specialization,
    string? ChiefComplaint,
    string Diagnosis,
    string? ClinicalNotes,
    bool CanEdit,
    bool CanAddAddendum,
    PrescriptionView? Prescription,
    IReadOnlyList<AddendumView> Addenda,
    IReadOnlyList<MedicalAttachmentView>? Attachments = null,
    bool CanUploadAttachments = false);

public sealed record PatientHistoryView(Guid PatientId, string PatientName, IReadOnlyList<MedicalRecordView> Visits);

public sealed record PrescriptionView(
    DateTimeOffset IssuedAtUtc,
    string? Notes,
    IReadOnlyList<PrescriptionItemView> Items);

public sealed record PrescriptionItemView(
    string MedicationName,
    string Dosage,
    string Frequency,
    int DurationDays,
    string? Instructions,
    bool IsActive,
    DateOnly ActiveUntil);

public sealed record AddendumView(DateTimeOffset CreatedAtUtc, string DoctorName, string Text);

public sealed record ActivePrescriptionView(
    Guid RecordId,
    DateOnly VisitDate,
    string DoctorName,
    string Specialization,
    string MedicationName,
    string Dosage,
    string Frequency,
    int DurationDays,
    string? Instructions,
    DateOnly ActiveUntil);
