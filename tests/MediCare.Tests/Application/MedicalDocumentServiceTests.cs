using System.Text;
using MediCare.Application.DTOs.Documents;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Storage;
using MediCare.Application.Services;
using MediCare.Application.Validation;
using MediCare.Domain.Entities;
using Moq;

namespace MediCare.Tests.Application;

public class MedicalDocumentServiceTests
{
    private readonly Mock<IMedicalDocumentRepository> _documents = new();
    private readonly Mock<IMedicalRecordRepository> _medicalRecords = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private MedicalDocumentService CreateService() => new(
        _documents.Object,
        _medicalRecords.Object,
        _doctors.Object,
        _patients.Object,
        _storage.Object,
        _unitOfWork.Object,
        new UploadMedicalDocumentRequestValidator());

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 sample document");

    private static UploadMedicalDocumentRequest Upload(string fileName, byte[] content) => new()
    {
        FileName = fileName,
        ContentType = "application/pdf",
        SizeInBytes = content.Length,
        Content = new MemoryStream(content)
    };

    private PatientProfile SetupPatient()
    {
        var patient = new PatientProfile(Guid.NewGuid());
        _patients
            .Setup(x => x.GetByUserIdAsync(patient.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);
        return patient;
    }

    private static DoctorProfile CreateApprovedDoctor()
    {
        var doctor = new DoctorProfile(Guid.NewGuid(), Guid.NewGuid(), "MED-1234", 100m, 5, "Bio");
        doctor.Approve(Guid.NewGuid());
        return doctor;
    }

    private void SetupDoctor(DoctorProfile doctor)
    {
        _doctors
            .Setup(x => x.GetByUserIdAsync(doctor.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
    }

    private void SetupAccess(DoctorProfile doctor, Guid patientId, bool allowed)
    {
        _medicalRecords
            .Setup(x => x.DoctorHasCompletedAppointmentWithPatientAsync(
                doctor.Id, patientId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);
    }

    private void VerifyNothingWasStored() =>
        _storage.Verify(x => x.SaveAsync(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);

    // ------------------------------------------------------------------ upload

    [Fact]
    public async Task UploadAsPatientAsync_RejectsUnsupportedFileTypes()
    {
        var patient = SetupPatient();

        var result = await CreateService().UploadAsPatientAsync(patient.UserId, Upload("virus.exe", PdfBytes));

        Assert.False(result.Succeeded);
        Assert.Contains("PDF", result.Error, StringComparison.OrdinalIgnoreCase);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task UploadAsPatientAsync_RejectsAFileWhoseContentDoesNotMatchItsExtension()
    {
        var patient = SetupPatient();
        var fakePdf = Encoding.ASCII.GetBytes("this is only text pretending to be a pdf");

        var result = await CreateService().UploadAsPatientAsync(patient.UserId, Upload("report.pdf", fakePdf));

        Assert.False(result.Succeeded);
        Assert.Contains("does not match", result.Error, StringComparison.OrdinalIgnoreCase);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task UploadAsPatientAsync_RejectsADeclaredSizeAboveTheLimit()
    {
        var patient = SetupPatient();
        var request = Upload("report.pdf", PdfBytes);
        request.SizeInBytes = MedicalDocumentRules.MaxSizeInBytes + 1;

        var result = await CreateService().UploadAsPatientAsync(patient.UserId, request);

        Assert.False(result.Succeeded);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task UploadAsPatientAsync_RejectsAFileThatIsLargerThanItsDeclaredSize()
    {
        var patient = SetupPatient();

        // The client claims 1 KB but really sends more than the limit.
        var content = new byte[(int)MedicalDocumentRules.MaxSizeInBytes + 1];
        Array.Copy(PdfBytes, content, PdfBytes.Length);
        var request = Upload("report.pdf", content);
        request.SizeInBytes = 1024;

        var result = await CreateService().UploadAsPatientAsync(patient.UserId, request);

        Assert.False(result.Succeeded);
        Assert.Contains("5 MB", result.Error, StringComparison.OrdinalIgnoreCase);
        VerifyNothingWasStored();
    }

    [Fact]
    public async Task UploadAsPatientAsync_StoresTheFileUnderAGeneratedName()
    {
        var patient = SetupPatient();
        string? savedName = null;
        _storage
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((name, _, _) => savedName = name)
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().UploadAsPatientAsync(patient.UserId, Upload("report.pdf", PdfBytes));

        Assert.True(result.Succeeded);
        Assert.NotNull(savedName);
        Assert.EndsWith(".pdf", savedName);
        Assert.DoesNotContain("report", savedName);
        _documents.Verify(x => x.AddAsync(
            It.Is<MedicalDocument>(d =>
                d.PatientProfileId == patient.Id &&
                d.UploadedByUserId == patient.UserId &&
                d.StoredFileName == savedName &&
                d.OriginalFileName == "report.pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsPatientAsync_KeepsOnlyTheFileNameFromAPath()
    {
        var patient = SetupPatient();
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().UploadAsPatientAsync(
            patient.UserId, Upload("../../secret/evil.pdf", PdfBytes));

        Assert.True(result.Succeeded);
        Assert.Equal("evil.pdf", result.Value!.OriginalFileName);
    }

    [Fact]
    public async Task UploadAsDoctorAsync_RequiresACompletedAppointmentWithThePatient()
    {
        var doctor = CreateApprovedDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, false);

        var result = await CreateService().UploadAsDoctorAsync(
            doctor.UserId, patientId, Upload("report.pdf", PdfBytes));

        Assert.False(result.Succeeded);
        VerifyNothingWasStored();
    }

    // ---------------------------------------------------------------- download

    [Fact]
    public async Task DownloadMyDocumentAsync_HidesADocumentOfAnotherPatient()
    {
        var patient = SetupPatient();
        var document = new MedicalDocument(
            Guid.NewGuid(), null, Guid.NewGuid(), "report.pdf", "abc.pdf", "application/pdf", 100);
        _documents
            .Setup(x => x.GetByIdAsync(document.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var result = await CreateService().DownloadMyDocumentAsync(patient.UserId, document.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        _storage.Verify(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadMyDocumentAsync_ReturnsTheFileToItsOwner()
    {
        var patient = SetupPatient();
        var document = new MedicalDocument(
            patient.Id, null, patient.UserId, "report.pdf", "abc.pdf", "application/pdf", 100);
        _documents
            .Setup(x => x.GetByIdAsync(document.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _storage
            .Setup(x => x.OpenReadAsync("abc.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(PdfBytes));

        var result = await CreateService().DownloadMyDocumentAsync(patient.UserId, document.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("report.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
    }

    [Fact]
    public async Task DownloadMyDocumentAsync_HidesADeletedDocument()
    {
        var patient = SetupPatient();
        var document = new MedicalDocument(
            patient.Id, null, patient.UserId, "report.pdf", "abc.pdf", "application/pdf", 100);
        document.Delete();
        _documents
            .Setup(x => x.GetByIdAsync(document.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var result = await CreateService().DownloadMyDocumentAsync(patient.UserId, document.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DownloadAsDoctorAsync_DeniesADoctorWithoutAccessToThePatient()
    {
        var doctor = CreateApprovedDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, false);
        var document = new MedicalDocument(
            patientId, null, Guid.NewGuid(), "report.pdf", "abc.pdf", "application/pdf", 100);
        _documents
            .Setup(x => x.GetByIdAsync(document.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var result = await CreateService().DownloadAsDoctorAsync(doctor.UserId, document.Id);

        Assert.False(result.Succeeded);
        _storage.Verify(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public async Task DeleteMyDocumentAsync_OnlyTheUploaderCanDelete()
    {
        var patient = SetupPatient();
        var uploadedByADoctor = new MedicalDocument(
            patient.Id, null, Guid.NewGuid(), "report.pdf", "abc.pdf", "application/pdf", 100);
        _documents
            .Setup(x => x.GetByIdAsync(uploadedByADoctor.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(uploadedByADoctor);

        var result = await CreateService().DeleteMyDocumentAsync(patient.UserId, uploadedByADoctor.Id);

        Assert.False(result.Succeeded);
        Assert.False(uploadedByADoctor.IsDeleted);
    }

    [Fact]
    public async Task DeleteMyDocumentAsync_SoftDeletesTheUploadersDocument()
    {
        var patient = SetupPatient();
        var document = new MedicalDocument(
            patient.Id, null, patient.UserId, "report.pdf", "abc.pdf", "application/pdf", 100);
        _documents
            .Setup(x => x.GetByIdAsync(document.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().DeleteMyDocumentAsync(patient.UserId, document.Id);

        Assert.True(result.Succeeded);
        Assert.True(document.IsDeleted);
    }
}
