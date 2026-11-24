using MediCare.Domain.Enums;

namespace MediCare.Domain.Entities;

/// <summary>
/// The visit record of one attendance attempt (one QueueEntry). The authoring doctor may edit it only on the visit's
/// clinic day; afterwards corrections are added as addenda, and the record itself is never silently changed.
/// </summary>
public sealed class MedicalRecord
{
    public const int MaxChiefComplaintLength = 1000;
    public const int MaxDiagnosisLength = 2000;
    public const int MaxClinicalNotesLength = 4000;

    private readonly List<MedicalRecordAddendum> _addenda = [];
    private readonly List<MedicalAttachment> _attachments = [];

    private MedicalRecord() { }

    public Guid Id { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid QueueEntryId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    /// <summary>Clinic-local date of the attendance (the queue date).</summary>
    public DateOnly VisitDate { get; private set; }
    public string? ChiefComplaint { get; private set; }
    public string Diagnosis { get; private set; } = string.Empty;
    public string? ClinicalNotes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Patient Patient { get; private set; } = null!;
    public Doctor Doctor { get; private set; } = null!;
    public Prescription? Prescription { get; private set; }
    public IReadOnlyCollection<MedicalRecordAddendum> Addenda => _addenda;
    public IReadOnlyCollection<MedicalAttachment> Attachments => _attachments;

    /// <summary>
    /// Attaches a stored file to the record. Allowed for any treating doctor at any time (lab results arrive later), so
    /// no visit-day rule applies. The caller has already checked access and written the file.
    /// </summary>
    public void AddAttachment(MedicalAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (attachment.MedicalRecordId != Id)
            throw new InvalidOperationException("The attachment belongs to a different medical record.");
        _attachments.Add(attachment);
    }

    /// <summary>Creates the record for an attendance that is in consultation or completed, by the doctor of that entry.</summary>
    public static MedicalRecord CreateForVisit(QueueEntry entry, Guid doctorId, string? chiefComplaint, string diagnosis, string? clinicalNotes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.DoctorId != doctorId)
            throw new InvalidOperationException("Only the doctor who saw this patient can create the visit record.");
        if (entry.Status is not (QueueEntryStatus.InConsultation or QueueEntryStatus.Completed))
            throw new InvalidOperationException("A visit record can be created only for a consultation that is in progress or completed.");

        var record = new MedicalRecord
        {
            Id = Guid.NewGuid(),
            PatientId = entry.PatientId,
            DoctorId = doctorId,
            QueueEntryId = entry.Id,
            AppointmentId = entry.AppointmentId,
            VisitDate = entry.QueueDate,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
        record.ApplyVisitText(chiefComplaint, diagnosis, clinicalNotes);
        return record;
    }

    /// <summary>True while the authoring doctor can still change the visit text and the prescription (same clinic day).</summary>
    public bool IsEditableBy(Guid doctorId, DateOnly clinicToday) =>
        doctorId == DoctorId && VisitDate == clinicToday;

    /// <summary>Edits the visit text. Allowed only for the authoring doctor on the visit's clinic day.</summary>
    public void UpdateVisit(Guid doctorId, DateOnly clinicToday, string? chiefComplaint, string diagnosis, string? clinicalNotes, DateTimeOffset nowUtc)
    {
        EnsureEditable(doctorId, clinicToday);
        ApplyVisitText(chiefComplaint, diagnosis, clinicalNotes);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    /// <summary>Adds an addendum. Addenda are allowed only after the visit day, so corrections never replace the original text.</summary>
    public MedicalRecordAddendum AddAddendum(Guid doctorId, DateOnly clinicToday, string text, DateTimeOffset nowUtc)
    {
        if (VisitDate >= clinicToday)
            throw new InvalidOperationException("Addenda can be added only after the visit day. While the day is open, edit the visit record instead.");

        var addendum = MedicalRecordAddendum.Create(Id, doctorId, text, nowUtc);
        _addenda.Add(addendum);
        return addendum;
    }

    /// <summary>Creates or replaces the prescription. Only the authoring doctor on the visit's clinic day may do this.</summary>
    public Prescription SetPrescription(Guid doctorId, DateOnly clinicToday, IReadOnlyList<PrescriptionItemDraft> items, string? notes, DateTimeOffset nowUtc)
    {
        EnsureEditable(doctorId, clinicToday);
        if (Prescription is null)
        {
            Prescription = Prescription.Create(Id, items, notes, nowUtc);
            UpdatedAtUtc = nowUtc.ToUniversalTime();
            return Prescription;
        }

        Prescription.Replace(items, notes, nowUtc);
        UpdatedAtUtc = nowUtc.ToUniversalTime();
        return Prescription;
    }

    private void EnsureEditable(Guid doctorId, DateOnly clinicToday)
    {
        if (doctorId != DoctorId)
            throw new InvalidOperationException("Only the doctor who created this visit record can change it.");
        if (VisitDate != clinicToday)
            throw new InvalidOperationException("The visit record is locked after its clinic day. Add an addendum instead.");
    }

    private void ApplyVisitText(string? chiefComplaint, string diagnosis, string? clinicalNotes)
    {
        var complaint = Normalize(chiefComplaint);
        var notes = Normalize(clinicalNotes);
        var trimmedDiagnosis = diagnosis?.Trim() ?? string.Empty;

        if (complaint is not null && complaint.Length > MaxChiefComplaintLength)
            throw new ArgumentException($"The chief complaint cannot exceed {MaxChiefComplaintLength} characters.", nameof(chiefComplaint));
        if (trimmedDiagnosis.Length == 0)
            throw new ArgumentException("A diagnosis is required.", nameof(diagnosis));
        if (trimmedDiagnosis.Length > MaxDiagnosisLength)
            throw new ArgumentException($"The diagnosis cannot exceed {MaxDiagnosisLength} characters.", nameof(diagnosis));
        if (notes is not null && notes.Length > MaxClinicalNotesLength)
            throw new ArgumentException($"The clinical notes cannot exceed {MaxClinicalNotesLength} characters.", nameof(clinicalNotes));

        ChiefComplaint = complaint;
        Diagnosis = trimmedDiagnosis;
        ClinicalNotes = notes;
    }

    internal static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
