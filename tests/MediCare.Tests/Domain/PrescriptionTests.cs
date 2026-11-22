using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Domain;

public class PrescriptionTests
{
    private static Prescription CreatePrescription() =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, null);

    [Fact]
    public void Prescription_RequiresPatientAndDoctor()
    {
        Assert.Throws<DomainException>(() => new Prescription(Guid.Empty, Guid.NewGuid(), null, null));
        Assert.Throws<DomainException>(() => new Prescription(Guid.NewGuid(), Guid.Empty, null, null));
    }

    [Fact]
    public void Prescription_RejectsEmptyMedicalRecordId()
    {
        Assert.Throws<DomainException>(() =>
            new Prescription(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null));
    }

    [Fact]
    public void Prescription_NormalizesNotes()
    {
        var prescription = new Prescription(Guid.NewGuid(), Guid.NewGuid(), null, "  After meals  ");

        Assert.Equal("After meals", prescription.Notes);
        Assert.Empty(prescription.Items);
        Assert.False(prescription.IsDeleted);
    }

    [Fact]
    public void Prescription_AddItemStoresTrimmedItemLinkedToPrescription()
    {
        var prescription = CreatePrescription();

        prescription.AddItem("  Amoxicillin  ", " 500 mg ", " 3 times daily ", 7, "  After meals  ");

        var item = Assert.Single(prescription.Items);
        Assert.Equal(prescription.Id, item.PrescriptionId);
        Assert.Equal("Amoxicillin", item.MedicationName);
        Assert.Equal("500 mg", item.Dosage);
        Assert.Equal("3 times daily", item.Frequency);
        Assert.Equal(7, item.DurationInDays);
        Assert.Equal("After meals", item.Instructions);
    }

    [Theory]
    [InlineData("", "500 mg", "daily", 5)]
    [InlineData("   ", "500 mg", "daily", 5)]
    [InlineData("Amoxicillin", "", "daily", 5)]
    [InlineData("Amoxicillin", "500 mg", " ", 5)]
    [InlineData("Amoxicillin", "500 mg", "daily", 0)]
    [InlineData("Amoxicillin", "500 mg", "daily", -3)]
    public void Prescription_AddItemRejectsInvalidItems(string name, string dosage, string frequency, int days)
    {
        var prescription = CreatePrescription();

        Assert.Throws<DomainException>(() => prescription.AddItem(name, dosage, frequency, days, null));
        Assert.Empty(prescription.Items);
    }

    [Fact]
    public void Prescription_DeleteIsSoftAndBlocksNewItems()
    {
        var prescription = CreatePrescription();
        prescription.AddItem("Amoxicillin", "500 mg", "daily", 5, null);

        prescription.Delete();

        Assert.True(prescription.IsDeleted);
        Assert.Throws<DomainException>(() => prescription.AddItem("Ibuprofen", "200 mg", "daily", 3, null));
        Assert.Single(prescription.Items);
    }
}
