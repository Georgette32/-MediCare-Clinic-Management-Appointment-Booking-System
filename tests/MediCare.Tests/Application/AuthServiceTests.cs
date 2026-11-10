using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Auth;
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

public class AuthServiceTests
{
    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<ISpecializationRepository> _specializations = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IAppUrlProvider> _urls = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private AuthService CreateService() => new(
        _identity.Object,
        _patients.Object,
        _doctors.Object,
        _specializations.Object,
        _unitOfWork.Object,
        _email.Object,
        _urls.Object,
        _currentUser.Object,
        new RegisterPatientRequestValidator(),
        new RegisterDoctorRequestValidator(),
        new LoginRequestValidator(),
        new ForgotPasswordRequestValidator(),
        new ResetPasswordRequestValidator(),
        new ChangePasswordRequestValidator(),
        new UpdateProfileRequestValidator(),
        NullLogger<AuthService>.Instance);

    private static RegisterDoctorRequest ValidDoctorRequest(Guid specializationId) => new()
    {
        FullName = "Dr. Alice Smith",
        Email = "doctor@example.com",
        PhoneNumber = " +1 555 0100 ",
        Password = "Password1",
        ConfirmPassword = "Password1",
        SpecializationId = specializationId,
        LicenseNumber = "MED-1234",
        ConsultationFee = 150m,
        YearsOfExperience = 8,
        Bio = "Cardiologist"
    };

    [Fact]
    public async Task RegisterDoctorAsync_CreatesPendingProfileWithDoctorRole()
    {
        var specialization = new Specialization("Cardiology");
        var userId = Guid.NewGuid();
        DoctorProfile? saved = null;
        string? assignedRole = null;

        _specializations
            .Setup(x => x.GetByIdAsync(specialization.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(specialization);
        _doctors
            .Setup(x => x.LicenseNumberExistsAsync("MED-1234", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _identity
            .Setup(x => x.FindByEmailAsync("doctor@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserInfo?)null);
        _identity.SetupGet(x => x.RequiresEmailConfirmation).Returns(false);
        _identity
            .Setup(x => x.CreateUserAsync(
                "doctor@example.com",
                "Password1",
                "Dr. Alice Smith",
                "+1 555 0100",
                Roles.Doctor,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((_, _, _, _, role, _) => assignedRole = role)
            .ReturnsAsync(Result.Ok(userId));
        _doctors
            .Setup(x => x.AddAsync(It.IsAny<DoctorProfile>(), It.IsAny<CancellationToken>()))
            .Callback<DoctorProfile, CancellationToken>((profile, _) => saved = profile)
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().RegisterDoctorAsync(ValidDoctorRequest(specialization.Id));

        Assert.True(result.Succeeded);
        Assert.Equal(Roles.Doctor, assignedRole);
        Assert.NotNull(saved);
        Assert.Equal(userId, saved!.UserId);
        Assert.Equal(DoctorApprovalStatus.Pending, saved.ApprovalStatus);
        Assert.False(saved.IsApproved);
        Assert.Equal(specialization.Id, saved.SpecializationId);
    }

    [Fact]
    public async Task RegisterDoctorAsync_RejectsInactiveSpecialization()
    {
        var specialization = new Specialization("Cardiology");
        specialization.Deactivate();

        _specializations
            .Setup(x => x.GetByIdAsync(specialization.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(specialization);

        var result = await CreateService().RegisterDoctorAsync(ValidDoctorRequest(specialization.Id));

        Assert.False(result.Succeeded);
        Assert.Contains("active specialization", result.Error, StringComparison.OrdinalIgnoreCase);
        _identity.Verify(x => x.CreateUserAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDoctorAsync_RejectsDuplicateLicense()
    {
        var specialization = new Specialization("Cardiology");

        _specializations
            .Setup(x => x.GetByIdAsync(specialization.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(specialization);
        _doctors
            .Setup(x => x.LicenseNumberExistsAsync("MED-1234", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService().RegisterDoctorAsync(ValidDoctorRequest(specialization.Id));

        Assert.False(result.Succeeded);
        Assert.Contains("license number", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterPatientAsync_CreatesPatientProfileWithPatientRole()
    {
        var userId = Guid.NewGuid();
        PatientProfile? saved = null;
        string? assignedRole = null;

        _identity
            .Setup(x => x.FindByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserInfo?)null);
        _identity.SetupGet(x => x.RequiresEmailConfirmation).Returns(false);
        _identity
            .Setup(x => x.CreateUserAsync(
                "alice@example.com",
                "Password1",
                "Alice Johnson",
                "+1 555 0100",
                Roles.Patient,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((_, _, _, _, role, _) => assignedRole = role)
            .ReturnsAsync(Result.Ok(userId));
        _patients
            .Setup(x => x.AddAsync(It.IsAny<PatientProfile>(), It.IsAny<CancellationToken>()))
            .Callback<PatientProfile, CancellationToken>((profile, _) => saved = profile)
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().RegisterPatientAsync(new RegisterPatientRequest
        {
            FullName = "Alice Johnson",
            Email = "alice@example.com",
            PhoneNumber = " +1 555 0100 ",
            Password = "Password1",
            ConfirmPassword = "Password1",
            DateOfBirth = new DateOnly(1990, 5, 1),
            Gender = Gender.Female,
            Address = "12 Main Street"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(Roles.Patient, assignedRole);
        Assert.NotNull(saved);
        Assert.Equal(userId, saved!.UserId);
        Assert.Equal(new DateOnly(1990, 5, 1), saved.DateOfBirth);
    }

    [Fact]
    public async Task RegisterPatientAsync_RejectsDuplicateEmail()
    {
        _identity
            .Setup(x => x.FindByEmailAsync("alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUserInfo(
                Guid.NewGuid(), "alice@example.com", "Existing", null, true, true, DateTime.UtcNow));

        var result = await CreateService().RegisterPatientAsync(new RegisterPatientRequest
        {
            FullName = "Alice Johnson",
            Email = "alice@example.com",
            Password = "Password1",
            ConfirmPassword = "Password1"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidRequest_DoesNotCallIdentity()
    {
        var outcome = await CreateService().LoginAsync(new LoginRequest
        {
            Email = "not-an-email",
            Password = string.Empty
        });

        Assert.Equal(SignInOutcome.InvalidCredentials, outcome);
        _identity.Verify(x => x.PasswordSignInAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithValidRequest_DelegatesToIdentity()
    {
        _identity
            .Setup(x => x.PasswordSignInAsync("alice@example.com", "Password1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SignInOutcome.Success);

        var outcome = await CreateService().LoginAsync(new LoginRequest
        {
            Email = "alice@example.com",
            Password = "Password1",
            RememberMe = true
        });

        Assert.Equal(SignInOutcome.Success, outcome);
    }
}
