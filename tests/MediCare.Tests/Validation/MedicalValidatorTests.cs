using MediCare.Application.DTOs.Documents;
using MediCare.Application.DTOs.MedicalRecords;
using MediCare.Application.DTOs.Patients;
using MediCare.Application.DTOs.Prescriptions;
using MediCare.Application.Validation;
using MediCare.Domain.Enums;

namespace MediCare.Tests.Validation;

public class MedicalValidatorTests
{
    // ------------------------------------------------------- patient profile

    private static UpdatePatientProfileRequest ValidProfile() => new()
    {
        FullName = "Sara Ali",
        PhoneNumber = "+20 100 000 0000",
        DateOfBirth = new DateOnly(1995, 5, 20),
        Gender = Gender.Female,
        Address = "12 Main Street",
        EmergencyContact = "Omar Ali +20 100 111 1111",
        BloodType = "O+"
    };

    [Fact]
    public void PatientProfile_AcceptsValidRequest()
    {
        var result = new UpdatePatientProfileRequestValidator().Validate(ValidProfile());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PatientProfile_RequiresName()
    {
        var request = ValidProfile();
        request.FullName = " ";

        Assert.False(new UpdatePatientProfileRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void PatientProfile_RejectsFutureDateOfBirth()
    {
        var request = ValidProfile();
        request.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        Assert.False(new UpdatePatientProfileRequestValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData("C+")]
    [InlineData("O")]
    [InlineData("positive")]
    public void PatientProfile_RejectsUnknownBloodType(string bloodType)
    {
        var request = ValidProfile();
        request.BloodType = bloodType;

        Assert.False(new UpdatePatientProfileRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void PatientProfile_AllowsMissingBloodType()
    {
        var request = ValidProfile();
        request.BloodType = null;

        Assert.True(new UpdatePatientProfileRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void PatientProfile_RejectsEmergencyContactLongerThanTheDatabaseColumn()
    {
        var request = ValidProfile();
        request.EmergencyContact = new string('x', 151);

        Assert.False(new UpdatePatientProfileRequestValidator().Validate(request).IsValid);
    }

    // ---------------------------------------------------------- medical record

    private static CreateMedicalRecordRequest ValidRecord() => new()
    {
        PatientProfileId = Guid.NewGuid(),
        Symptoms = "Cough",
        Diagnosis = "Bronchitis"
    };

    [Fact]
    public void MedicalRecord_AcceptsValidRequest()
    {
        Assert.True(new CreateMedicalRecordRequestValidator().Validate(ValidRecord()).IsValid);
    }

    [Fact]
    public void MedicalRecord_RequiresPatientSymptomsAndDiagnosis()
    {
        var validator = new CreateMedicalRecordRequestValidator();

        var noPatient = ValidRecord();
        noPatient.PatientProfileId = Guid.Empty;
        Assert.False(validator.Validate(noPatient).IsValid);

        var noSymptoms = ValidRecord();
        noSymptoms.Symptoms = "";
        Assert.False(validator.Validate(noSymptoms).IsValid);

        var noDiagnosis = ValidRecord();
        noDiagnosis.Diagnosis = "  ";
        Assert.False(validator.Validate(noDiagnosis).IsValid);
    }

    [Fact]
    public void MedicalRecord_RejectsTextLongerThanTheLimit()
    {
        var request = ValidRecord();
        request.DoctorNotes = new string('n', 2001);

        Assert.False(new CreateMedicalRecordRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void MedicalRecordUpdate_RequiresTheRecord()
    {
        var request = new UpdateMedicalRecordRequest
        {
            RecordId = Guid.Empty,
            Symptoms = "Cough",
            Diagnosis = "Bronchitis"
        };

        Assert.False(new UpdateMedicalRecordRequestValidator().Validate(request).IsValid);
    }

    // ------------------------------------------------------------ prescription

    private static PrescriptionItemRequest ValidItem() => new()
    {
        MedicationName = "Amoxicillin",
        Dosage = "500 mg",
        Frequency = "3 times daily",
        DurationInDays = 7
    };

    private static CreatePrescriptionRequest ValidPrescription() => new()
    {
        PatientProfileId = Guid.NewGuid(),
        Items = new List<PrescriptionItemRequest> { ValidItem() }
    };

    [Fact]
    public void Prescription_AcceptsValidRequest()
    {
        Assert.True(new CreatePrescriptionRequestValidator().Validate(ValidPrescription()).IsValid);
    }

    [Fact]
    public void Prescription_RequiresAtLeastOneMedication()
    {
        var request = ValidPrescription();
        request.Items.Clear();

        Assert.False(new CreatePrescriptionRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Prescription_RejectsMoreThanTwentyMedications()
    {
        var request = ValidPrescription();
        request.Items = Enumerable.Range(0, 21).Select(_ => ValidItem()).ToList();

        Assert.False(new CreatePrescriptionRequestValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void PrescriptionItem_RejectsInvalidDuration(int days)
    {
        var request = ValidPrescription();
        request.Items[0].DurationInDays = days;

        Assert.False(new CreatePrescriptionRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void PrescriptionItem_RequiresNameDosageAndFrequency()
    {
        var validator = new CreatePrescriptionRequestValidator();

        var noName = ValidPrescription();
        noName.Items[0].MedicationName = "";
        Assert.False(validator.Validate(noName).IsValid);

        var noDosage = ValidPrescription();
        noDosage.Items[0].Dosage = " ";
        Assert.False(validator.Validate(noDosage).IsValid);

        var noFrequency = ValidPrescription();
        noFrequency.Items[0].Frequency = "";
        Assert.False(validator.Validate(noFrequency).IsValid);
    }

    // ---------------------------------------------------------------- documents

    private static UploadMedicalDocumentRequest ValidUpload(string fileName = "report.pdf") => new()
    {
        FileName = fileName,
        ContentType = "application/pdf",
        SizeInBytes = 1024,
        Content = new MemoryStream(new byte[] { 1 })
    };

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("scan.PNG")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    public void Document_AcceptsAllowedTypes(string fileName)
    {
        Assert.True(new UploadMedicalDocumentRequestValidator().Validate(ValidUpload(fileName)).IsValid);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("notes.txt")]
    [InlineData("page.html")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void Document_RejectsOtherTypes(string fileName)
    {
        Assert.False(new UploadMedicalDocumentRequestValidator().Validate(ValidUpload(fileName)).IsValid);
    }

    [Fact]
    public void Document_RejectsEmptyAndOversizedFiles()
    {
        var validator = new UploadMedicalDocumentRequestValidator();

        var empty = ValidUpload();
        empty.SizeInBytes = 0;
        Assert.False(validator.Validate(empty).IsValid);

        var tooBig = ValidUpload();
        tooBig.SizeInBytes = MedicalDocumentRules.MaxSizeInBytes + 1;
        Assert.False(validator.Validate(tooBig).IsValid);

        var exactlyTheLimit = ValidUpload();
        exactlyTheLimit.SizeInBytes = MedicalDocumentRules.MaxSizeInBytes;
        Assert.True(validator.Validate(exactlyTheLimit).IsValid);
    }

    [Fact]
    public void Document_SignatureCheckMatchesFileType()
    {
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var text = System.Text.Encoding.ASCII.GetBytes("just some text");

        Assert.True(MedicalDocumentRules.HasValidSignature(".pdf", pdf));
        Assert.True(MedicalDocumentRules.HasValidSignature(".png", png));
        Assert.True(MedicalDocumentRules.HasValidSignature(".jpg", jpg));
        Assert.True(MedicalDocumentRules.HasValidSignature(".jpeg", jpg));

        Assert.False(MedicalDocumentRules.HasValidSignature(".pdf", text));
        Assert.False(MedicalDocumentRules.HasValidSignature(".pdf", png));
        Assert.False(MedicalDocumentRules.HasValidSignature(".png", pdf));
        Assert.False(MedicalDocumentRules.HasValidSignature(".exe", pdf));
    }
}
