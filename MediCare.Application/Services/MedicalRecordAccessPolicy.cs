using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

/// <summary>Roles that can reach medical content. Receptionist and Admin are listed so that the matrix is explicit.</summary>
public enum MedicalViewerRole
{
    Patient,
    Doctor,
    Receptionist,
    Admin,
    Other
}

/// <summary>
/// The viewer as the server resolved it: the role from the signed-in identity plus the profile id that role owns.
/// Ids never come from a form.
/// </summary>
public sealed record MedicalViewer(MedicalViewerRole Role, Guid? PatientId, Guid? DoctorId);

/// <summary>
/// The single medical-content access policy. Every read and write of medical data asks this class.
/// Matrix (TECHNICAL_SPECIFICATION section 4): patient = own records only; doctor = patients with a treating
/// relationship; receptionist = no medical content; admin = no access until an admin policy is defined (open question).
/// </summary>
public static class MedicalRecordAccessPolicy
{
    /// <summary>
    /// A treating relationship is at least one In-Consultation or Completed queue entry, or a Confirmed or Completed
    /// appointment, between the doctor and the patient. The relationship itself is computed by the repository.
    /// </summary>
    public static bool CanViewPatientHistory(MedicalViewer viewer, Guid patientId, bool hasTreatingRelationship) => viewer.Role switch
    {
        MedicalViewerRole.Patient => viewer.PatientId == patientId,
        MedicalViewerRole.Doctor => viewer.DoctorId is not null && hasTreatingRelationship,
        _ => false
    };

    /// <summary>Any doctor with a treating relationship may add an addendum. The visit-day rule is enforced by the domain.</summary>
    public static bool CanAddAddendum(MedicalViewer viewer, bool hasTreatingRelationship) =>
        viewer.Role == MedicalViewerRole.Doctor && viewer.DoctorId is not null && hasTreatingRelationship;

    /// <summary>
    /// Any doctor with a treating relationship may attach files, at any time (lab results arrive later). Attachments can
    /// never be deleted, so there is no matching delete rule.
    /// </summary>
    public static bool CanUploadAttachment(MedicalViewer viewer, bool hasTreatingRelationship) =>
        viewer.Role == MedicalViewerRole.Doctor && viewer.DoctorId is not null && hasTreatingRelationship;

    /// <summary>Only the authoring doctor, and only on the visit's clinic day.</summary>
    public static bool CanEditVisit(MedicalViewer viewer, MedicalRecord record, DateOnly clinicToday) =>
        viewer.Role == MedicalViewerRole.Doctor && viewer.DoctorId is Guid doctorId && record.IsEditableBy(doctorId, clinicToday);
}
