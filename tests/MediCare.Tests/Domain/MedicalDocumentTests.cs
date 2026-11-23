using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Domain;

public class MedicalDocumentTests
{
    private static MedicalDocument CreateDocument() =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "report.pdf", "abc123.pdf", "application/pdf", 1024);

    [Fact]
    public void MedicalDocument_StoresMetadata()
    {
        var patientId = Guid.NewGuid();
        var uploaderId = Guid.NewGuid();

        var document = new MedicalDocument(
            patientId, null, uploaderId, "  report.pdf  ", "  abc123.pdf  ", "  application/pdf  ", 2048);

        Assert.Equal(patientId, document.PatientProfileId);
        Assert.Equal(uploaderId, document.UploadedByUserId);
        Assert.Equal("report.pdf", document.OriginalFileName);
        Assert.Equal("abc123.pdf", document.StoredFileName);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(2048, document.SizeInBytes);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public void MedicalDocument_RequiresPatientAndUploader()
    {
        Assert.Throws<DomainException>(() =>
            new MedicalDocument(Guid.Empty, null, Guid.NewGuid(), "a.pdf", "b.pdf", "application/pdf", 10));

        Assert.Throws<DomainException>(() =>
            new MedicalDocument(Guid.NewGuid(), null, Guid.Empty, "a.pdf", "b.pdf", "application/pdf", 10));
    }

    [Fact]
    public void MedicalDocument_RejectsEmptyMedicalRecordId()
    {
        Assert.Throws<DomainException>(() =>
            new MedicalDocument(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "a.pdf", "b.pdf", "application/pdf", 10));
    }

    [Theory]
    [InlineData("", "b.pdf", "application/pdf", 10)]
    [InlineData("a.pdf", " ", "application/pdf", 10)]
    [InlineData("a.pdf", "b.pdf", "", 10)]
    [InlineData("a.pdf", "b.pdf", "application/pdf", 0)]
    [InlineData("a.pdf", "b.pdf", "application/pdf", -1)]
    public void MedicalDocument_RejectsInvalidMetadata(string original, string stored, string contentType, long size)
    {
        Assert.Throws<DomainException>(() =>
            new MedicalDocument(Guid.NewGuid(), null, Guid.NewGuid(), original, stored, contentType, size));
    }

    [Fact]
    public void MedicalDocument_DeleteIsSoft()
    {
        var document = CreateDocument();

        document.Delete();

        Assert.True(document.IsDeleted);
        Assert.NotNull(document.UpdatedAtUtc);
    }
}
