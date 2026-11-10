using MediCare.Application.Common;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Services;
using MediCare.Application.Validation;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MediCare.Tests.Application;

public class DoctorManagementServiceTests
{
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<ISpecializationRepository> _specializations = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IAppUrlProvider> _urls = new();

    private DoctorManagementService CreateService() => new(
        _doctors.Object,
        _specializations.Object,
        _unitOfWork.Object,
        _identity.Object,
        _email.Object,
        _urls.Object,
        new UpdateDoctorRequestValidator(),
        NullLogger<DoctorManagementService>.Instance);

    private static DoctorProfile CreateDoctor(Guid userId) =>
        new(userId, Guid.NewGuid(), "MED-1234", 100m, 5, "Bio");

    private void SetupNotification(Guid userId)
    {
        _identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUserInfo(
                userId, "doctor@example.com", "Dr. Alice Smith", null, true, true, DateTime.UtcNow));
        _urls.Setup(x => x.LoginUrl()).Returns("http://localhost/Account/Login");
        _email
            .Setup(x => x.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ApproveAsync_RequiresAdministrator()
    {
        var result = await CreateService().ApproveAsync(Guid.Empty, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Contains("administrator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAsync_ApprovesPendingDoctorAndNotifies()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var doctor = CreateDoctor(userId);

        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        SetupNotification(userId);

        var result = await CreateService().ApproveAsync(adminId, doctor.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(DoctorApprovalStatus.Approved, doctor.ApprovalStatus);
        Assert.Equal(adminId, doctor.ReviewedByUserId);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(x => x.SendAsync(
            "doctor@example.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_WhenAlreadyApproved_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var doctor = CreateDoctor(userId);
        doctor.Approve(Guid.NewGuid());

        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);

        var result = await CreateService().ApproveAsync(Guid.NewGuid(), doctor.Id);

        Assert.True(result.Succeeded);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_ReturnsFailureWhenDoctorMissing()
    {
        _doctors
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DoctorProfile?)null);

        var result = await CreateService().ApproveAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectAsync_RequiresReason()
    {
        var result = await CreateService().RejectAsync(Guid.NewGuid(), Guid.NewGuid(), "   ");

        Assert.False(result.Succeeded);
        Assert.Contains("reason", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectAsync_SetsRejectedStateAndNotifies()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var doctor = CreateDoctor(userId);

        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        SetupNotification(userId);

        var result = await CreateService().RejectAsync(adminId, doctor.Id, "Documents missing.");

        Assert.True(result.Succeeded);
        Assert.Equal(DoctorApprovalStatus.Rejected, doctor.ApprovalStatus);
        Assert.Equal("Documents missing.", doctor.RejectionReason);
        _email.Verify(x => x.SendAsync(
            "doctor@example.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAccountActiveAsync_CannotDeactivateSelf()
    {
        var adminId = Guid.NewGuid();
        var doctor = CreateDoctor(adminId);

        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);

        var result = await CreateService().SetAccountActiveAsync(adminId, doctor.Id, false);

        Assert.False(result.Succeeded);
        Assert.Contains("your own account", result.Error, StringComparison.OrdinalIgnoreCase);
        _identity.Verify(x => x.SetActiveAsync(
            It.IsAny<Guid>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidRequestBeforeLoadingDoctor()
    {
        var result = await CreateService().UpdateAsync(Guid.NewGuid(), new UpdateDoctorRequest());

        Assert.False(result.Succeeded);
        _doctors.Verify(x => x.GetByIdAsync(
            It.IsAny<Guid>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDuplicateLicense()
    {
        var userId = Guid.NewGuid();
        var doctor = CreateDoctor(userId);
        var specialization = new Specialization("Cardiology");

        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _specializations
            .Setup(x => x.GetByIdAsync(specialization.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(specialization);
        _doctors
            .Setup(x => x.LicenseNumberExistsAsync("MED-9999", doctor.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService().UpdateAsync(Guid.NewGuid(), new UpdateDoctorRequest
        {
            DoctorId = doctor.Id,
            FullName = "Dr. Alice Smith",
            PhoneNumber = "+1 555 0100",
            SpecializationId = specialization.Id,
            LicenseNumber = "MED-9999",
            ConsultationFee = 200m,
            YearsOfExperience = 9
        });

        Assert.False(result.Succeeded);
        Assert.Contains("license number", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesAccountAndProfessionalDetails()
    {
        var userId = Guid.NewGuid();
        var doctor = CreateDoctor(userId);
        var specialization = new Specialization("Cardiology");
        var adminId = Guid.NewGuid();

        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _specializations
            .Setup(x => x.GetByIdAsync(specialization.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(specialization);
        _doctors
            .Setup(x => x.LicenseNumberExistsAsync("MED-9999", doctor.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _identity
            .Setup(x => x.UpdateAccountAsync(userId, "Dr. Alice Smith", "+1 555 0100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().UpdateAsync(adminId, new UpdateDoctorRequest
        {
            DoctorId = doctor.Id,
            FullName = "Dr. Alice Smith",
            PhoneNumber = "+1 555 0100",
            SpecializationId = specialization.Id,
            LicenseNumber = "MED-9999",
            ConsultationFee = 200m,
            YearsOfExperience = 9,
            Bio = "Updated"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("MED-9999", doctor.LicenseNumber);
        Assert.Equal(200m, doctor.ConsultationFee);
        Assert.Equal(specialization.Id, doctor.SpecializationId);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
