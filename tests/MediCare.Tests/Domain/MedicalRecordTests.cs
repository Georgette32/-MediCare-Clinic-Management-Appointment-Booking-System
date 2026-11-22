using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Domain;

public class MedicalRecordTests
{
    private static MedicalRecord CreateRecord() =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, "Cough", "Bronchitis", null, null);

    [Fact]
    public void MedicalRecord_RequiresPatientAndDoctor()
    {
        Assert.Throws<DomainException>(() =>
            new MedicalRecord(Guid.Empty, Guid.NewGuid(), null, "Cough", "Bronchitis", null, null));

        Assert.Throws<DomainException>(() =>
            new MedicalRecord(Guid.NewGuid(), Guid.Empty, null, "Cough", "Bronchitis", null, null));
    }

    [Fact]
    public void MedicalRecord_RejectsEmptyAppointmentId()
    {
        Assert.Throws<DomainException>(() =>
            new MedicalRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Cough", "Bronchitis", null, null));
    }

    [Fact]
    public void MedicalRecord_RequiresSymptomsAndDiagnosis()
    {
        Assert.Throws<DomainException>(() =>
            new MedicalRecord(Guid.NewGuid(), Guid.NewGuid(), null, "  ", "Bronchitis", null, null));

        Assert.Throws<DomainException>(() =>
            new MedicalRecord(Guid.NewGuid(), Guid.NewGuid(), null, "Cough", "", null, null));
    }

    [Fact]
    public void MedicalRecord_NormalizesText()
    {
        var patientId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        var record = new MedicalRecord(
            patientId,
            doctorId,
            appointmentId,
            "  Cough  ",
            "  Bronchitis  ",
            "   ",
            "  Rest  ");

        Assert.Equal(patientId, record.PatientProfileId);
        Assert.Equal(doctorId, record.DoctorProfileId);
        Assert.Equal(appointmentId, record.AppointmentId);
        Assert.Equal("Cough", record.Symptoms);
        Assert.Equal("Bronchitis", record.Diagnosis);
        Assert.Null(record.TreatmentPlan);
        Assert.Equal("Rest", record.DoctorNotes);
        Assert.False(record.IsDeleted);
    }

    [Fact]
    public void MedicalRecord_UpdateChangesDetailsAndMarksUpdated()
    {
        var record = CreateRecord();
        Assert.Null(record.UpdatedAtUtc);

        record.Update("Fever", "Flu", "Fluids", "Return in a week");

        Assert.Equal("Fever", record.Symptoms);
        Assert.Equal("Flu", record.Diagnosis);
        Assert.Equal("Fluids", record.TreatmentPlan);
        Assert.Equal("Return in a week", record.DoctorNotes);
        Assert.NotNull(record.UpdatedAtUtc);
    }

    [Fact]
    public void MedicalRecord_UpdateRejectsInvalidDetails()
    {
        var record = CreateRecord();

        Assert.Throws<DomainException>(() => record.Update("", "Flu", null, null));
        Assert.Throws<DomainException>(() => record.Update("Fever", " ", null, null));
        Assert.Equal("Cough", record.Symptoms);
    }

    [Fact]
    public void MedicalRecord_DeleteIsSoftAndBlocksFurtherChanges()
    {
        var record = CreateRecord();

        record.Delete();

        Assert.True(record.IsDeleted);
        Assert.Throws<DomainException>(() => record.Update("Fever", "Flu", null, null));
    }
}
