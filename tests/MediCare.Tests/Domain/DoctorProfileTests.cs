using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Domain;

public class DoctorProfileTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SpecializationId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private static DoctorProfile CreateProfile() =>
        new(UserId, SpecializationId, "MED-1234", 100m, 5, "Experienced clinician.");

    [Fact]
    public void NewProfile_IsPending()
    {
        var profile = CreateProfile();

        Assert.Equal(DoctorApprovalStatus.Pending, profile.ApprovalStatus);
        Assert.False(profile.IsApproved);
        Assert.Null(profile.ReviewedAtUtc);
        Assert.Null(profile.ReviewedByUserId);
    }

    [Fact]
    public void Constructor_RejectsEmptyUser()
    {
        Assert.Throws<DomainException>(() =>
            new DoctorProfile(Guid.Empty, SpecializationId, "MED-1234", 100m, 5));
    }

    [Fact]
    public void Constructor_RejectsEmptySpecialization()
    {
        Assert.Throws<DomainException>(() =>
            new DoctorProfile(UserId, Guid.Empty, "MED-1234", 100m, 5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Constructor_RejectsNegativeFee(decimal fee)
    {
        Assert.Throws<DomainException>(() =>
            new DoctorProfile(UserId, SpecializationId, "MED-1234", fee, 5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    public void Constructor_RejectsUnrealisticExperience(int years)
    {
        Assert.Throws<DomainException>(() =>
            new DoctorProfile(UserId, SpecializationId, "MED-1234", 100m, years));
    }

    [Fact]
    public void Constructor_NormalizesProfessionalFields()
    {
        var profile = new DoctorProfile(UserId, SpecializationId, "  MED-1234  ", 100.456m, 5, "  Bio  ");

        Assert.Equal("MED-1234", profile.LicenseNumber);
        Assert.Equal(100.46m, profile.ConsultationFee);
        Assert.Equal("Bio", profile.Bio);
    }

    [Fact]
    public void Approve_SetsApprovedStateAndReviewer()
    {
        var profile = CreateProfile();

        profile.Approve(AdminId);

        Assert.Equal(DoctorApprovalStatus.Approved, profile.ApprovalStatus);
        Assert.True(profile.IsApproved);
        Assert.Equal(AdminId, profile.ReviewedByUserId);
        Assert.NotNull(profile.ReviewedAtUtc);
        Assert.Null(profile.RejectionReason);
    }

    [Fact]
    public void Approve_RequiresAdministrator()
    {
        var profile = CreateProfile();

        Assert.Throws<DomainException>(() => profile.Approve(Guid.Empty));
    }

    [Fact]
    public void Reject_RequiresReason()
    {
        var profile = CreateProfile();

        Assert.Throws<DomainException>(() => profile.Reject(AdminId, "   "));
    }

    [Fact]
    public void Reject_SetsRejectedStateAndReason()
    {
        var profile = CreateProfile();

        profile.Reject(AdminId, "  License could not be verified.  ");

        Assert.Equal(DoctorApprovalStatus.Rejected, profile.ApprovalStatus);
        Assert.False(profile.IsApproved);
        Assert.Equal("License could not be verified.", profile.RejectionReason);
        Assert.Equal(AdminId, profile.ReviewedByUserId);
    }

    [Fact]
    public void Suspend_RequiresReason()
    {
        var profile = CreateProfile();

        Assert.Throws<DomainException>(() => profile.Suspend(AdminId, string.Empty));
    }

    [Fact]
    public void Suspend_SetsSuspendedState()
    {
        var profile = CreateProfile();
        profile.Approve(AdminId);

        profile.Suspend(AdminId, "Under investigation.");

        Assert.Equal(DoctorApprovalStatus.Suspended, profile.ApprovalStatus);
        Assert.False(profile.IsApproved);
        Assert.Equal("Under investigation.", profile.RejectionReason);
    }

    [Fact]
    public void ReturnToPending_ClearsReviewMetadata()
    {
        var profile = CreateProfile();
        profile.Reject(AdminId, "Missing documents.");

        profile.ReturnToPending();

        Assert.Equal(DoctorApprovalStatus.Pending, profile.ApprovalStatus);
        Assert.Null(profile.RejectionReason);
        Assert.Null(profile.ReviewedAtUtc);
        Assert.Null(profile.ReviewedByUserId);
    }

    [Fact]
    public void UpdateProfessionalDetails_ValidatesInput()
    {
        var profile = CreateProfile();

        Assert.Throws<DomainException>(() =>
            profile.UpdateProfessionalDetails(Guid.Empty, "MED-1234", 100m, 5, null));
        Assert.Throws<DomainException>(() =>
            profile.UpdateProfessionalDetails(SpecializationId, " ", 100m, 5, null));
        Assert.Throws<DomainException>(() =>
            profile.UpdateProfessionalDetails(SpecializationId, "MED-1234", -1m, 5, null));
        Assert.Throws<DomainException>(() =>
            profile.UpdateProfessionalDetails(SpecializationId, "MED-1234", 100m, 99, null));
    }

    [Fact]
    public void UpdateProfessionalDetails_ChangesValues()
    {
        var profile = CreateProfile();
        var newSpecialization = Guid.NewGuid();

        profile.UpdateProfessionalDetails(newSpecialization, "MED-9999", 250.5m, 12, "  Updated bio  ");

        Assert.Equal(newSpecialization, profile.SpecializationId);
        Assert.Equal("MED-9999", profile.LicenseNumber);
        Assert.Equal(250.5m, profile.ConsultationFee);
        Assert.Equal(12, profile.YearsOfExperience);
        Assert.Equal("Updated bio", profile.Bio);
    }
}
