using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Domain;

public class PatientProfileAndSupportingEntityTests
{
    [Fact]
    public void PatientProfile_RequiresUser()
    {
        Assert.Throws<DomainException>(() => new PatientProfile(Guid.Empty));
    }

    [Fact]
    public void PatientProfile_RejectsFutureDateOfBirth()
    {
        var profile = new PatientProfile(Guid.NewGuid());

        Assert.Throws<DomainException>(() => profile.UpdateMedicalInformation(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Gender.Female,
            null,
            null,
            null));
    }

    [Fact]
    public void PatientProfile_NormalizesMedicalInformation()
    {
        var profile = new PatientProfile(Guid.NewGuid());

        profile.UpdateMedicalInformation(
            new DateOnly(1990, 1, 1),
            Gender.Male,
            "  12 Main Street  ",
            "  +1 555 0100  ",
            "  O+  ");

        Assert.Equal("12 Main Street", profile.Address);
        Assert.Equal("+1 555 0100", profile.EmergencyContact);
        Assert.Equal("O+", profile.BloodType);
        Assert.Equal(Gender.Male, profile.Gender);
    }

    [Fact]
    public void Specialization_ValidatesNameAndNormalizesDescription()
    {
        Assert.Throws<DomainException>(() => new Specialization("  "));

        var specialization = new Specialization("  Cardiology  ", "  Heart care  ");

        Assert.Equal("Cardiology", specialization.Name);
        Assert.Equal("Heart care", specialization.Description);
        Assert.True(specialization.IsActive);
    }

    [Fact]
    public void Specialization_DeactivateAndActivate()
    {
        var specialization = new Specialization("Dermatology");

        specialization.Deactivate();
        Assert.False(specialization.IsActive);

        specialization.Activate();
        Assert.True(specialization.IsActive);
    }

    [Fact]
    public void DoctorAvailability_ValidatesWindowAndSlot()
    {
        var doctorId = Guid.NewGuid();

        Assert.Throws<DomainException>(() => new DoctorAvailability(
            doctorId, DayOfWeek.Monday, new TimeOnly(12, 0), new TimeOnly(12, 0)));
        Assert.Throws<DomainException>(() => new DoctorAvailability(
            doctorId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0), 5));

        var availability = new DoctorAvailability(
            doctorId, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0), 30);

        Assert.True(availability.IsActive);
        Assert.Equal(30, availability.SlotDurationMinutes);
    }
}
