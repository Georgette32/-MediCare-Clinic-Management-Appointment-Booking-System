using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Tests;

/// <summary>The full viewer x resource matrix of the medical access policy (TECHNICAL_SPECIFICATION section 4).</summary>
public sealed class MedicalRecordAccessPolicyTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid OtherPatientId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly Guid OtherDoctorId = Guid.NewGuid();

    private static readonly MedicalViewer OwnPatient = new(MedicalViewerRole.Patient, PatientId, null);
    private static readonly MedicalViewer OtherPatient = new(MedicalViewerRole.Patient, OtherPatientId, null);
    private static readonly MedicalViewer Treating = new(MedicalViewerRole.Doctor, null, DoctorId);
    private static readonly MedicalViewer Unrelated = new(MedicalViewerRole.Doctor, null, OtherDoctorId);
    private static readonly MedicalViewer Receptionist = new(MedicalViewerRole.Receptionist, null, null);
    private static readonly MedicalViewer Admin = new(MedicalViewerRole.Admin, null, null);
    private static readonly MedicalViewer Unknown = new(MedicalViewerRole.Other, null, null);

    [Fact]
    public void Patients_see_their_own_history_only()
    {
        Assert.True(MedicalRecordAccessPolicy.CanViewPatientHistory(OwnPatient, PatientId, hasTreatingRelationship: false));
        Assert.False(MedicalRecordAccessPolicy.CanViewPatientHistory(OtherPatient, PatientId, hasTreatingRelationship: false));
        Assert.False(MedicalRecordAccessPolicy.CanViewPatientHistory(new MedicalViewer(MedicalViewerRole.Patient, null, null), PatientId, false));
    }

    [Fact]
    public void Doctors_see_history_only_with_a_treating_relationship()
    {
        Assert.True(MedicalRecordAccessPolicy.CanViewPatientHistory(Treating, PatientId, hasTreatingRelationship: true));
        Assert.False(MedicalRecordAccessPolicy.CanViewPatientHistory(Unrelated, PatientId, hasTreatingRelationship: false));
        // A relationship flag without a doctor profile on the viewer grants nothing.
        Assert.False(MedicalRecordAccessPolicy.CanViewPatientHistory(new MedicalViewer(MedicalViewerRole.Doctor, null, null), PatientId, hasTreatingRelationship: true));
    }

    [Theory]
    [InlineData(MedicalViewerRole.Receptionist)]
    [InlineData(MedicalViewerRole.Admin)]
    [InlineData(MedicalViewerRole.Other)]
    public void Receptionists_and_admins_get_no_medical_content_in_this_step(MedicalViewerRole role)
    {
        var viewer = new MedicalViewer(role, null, null);

        Assert.False(MedicalRecordAccessPolicy.CanViewPatientHistory(viewer, PatientId, hasTreatingRelationship: true));
        Assert.False(MedicalRecordAccessPolicy.CanAddAddendum(viewer, hasTreatingRelationship: true));
    }

    [Fact]
    public void Only_a_treating_doctor_can_add_an_addendum()
    {
        Assert.True(MedicalRecordAccessPolicy.CanAddAddendum(Treating, hasTreatingRelationship: true));
        Assert.False(MedicalRecordAccessPolicy.CanAddAddendum(Treating, hasTreatingRelationship: false));
        Assert.False(MedicalRecordAccessPolicy.CanAddAddendum(Unrelated, hasTreatingRelationship: false));
        Assert.False(MedicalRecordAccessPolicy.CanAddAddendum(OwnPatient, hasTreatingRelationship: true));
        Assert.False(MedicalRecordAccessPolicy.CanAddAddendum(Unknown, hasTreatingRelationship: true));
    }

    [Fact]
    public void Only_the_authoring_doctor_can_edit_and_only_on_the_visit_day()
    {
        var record = MedicalRecord.CreateForVisit(
            QueueEntryFor(DoctorId, Today), DoctorId, null, "Flu", null, new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));

        Assert.True(MedicalRecordAccessPolicy.CanEditVisit(Treating, record, Today));
        Assert.False(MedicalRecordAccessPolicy.CanEditVisit(Treating, record, Today.AddDays(1)));
        Assert.False(MedicalRecordAccessPolicy.CanEditVisit(Unrelated, record, Today));
        Assert.False(MedicalRecordAccessPolicy.CanEditVisit(OwnPatient, record, Today));
        Assert.False(MedicalRecordAccessPolicy.CanEditVisit(Receptionist, record, Today));
        Assert.False(MedicalRecordAccessPolicy.CanEditVisit(Admin, record, Today));
    }

    private static QueueEntry QueueEntryFor(Guid doctorId, DateOnly date)
    {
        var arrived = new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 55)), TimeSpan.Zero);
        var entry = QueueEntry.CreateScheduled(PatientId, doctorId, Guid.NewGuid(), date, arrived);
        entry.Call(arrived.AddMinutes(5));
        entry.StartConsultation(arrived.AddMinutes(10));
        return entry;
    }
}
