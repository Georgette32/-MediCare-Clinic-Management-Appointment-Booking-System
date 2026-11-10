using FluentValidation.TestHelper;
using MediCare.Application.DTOs.Auth;
using MediCare.Application.DTOs.Specializations;
using MediCare.Application.DTOs.Users;
using MediCare.Application.Validation;

namespace MediCare.Tests.Validation;

public class ValidatorTests
{
    private static RegisterPatientRequest ValidPatientRequest() => new()
    {
        FullName = "Alice Johnson",
        Email = "alice@example.com",
        PhoneNumber = "+1 555 0100",
        Password = "Password1",
        ConfirmPassword = "Password1",
        DateOfBirth = new DateOnly(1990, 5, 1),
        Address = "12 Main Street"
    };

    private static RegisterDoctorRequest ValidDoctorRequest() => new()
    {
        FullName = "Dr. Alice Smith",
        Email = "doctor@example.com",
        PhoneNumber = "+1 555 0100",
        Password = "Password1",
        ConfirmPassword = "Password1",
        SpecializationId = Guid.NewGuid(),
        LicenseNumber = "MED-1234",
        ConsultationFee = 100m,
        YearsOfExperience = 5
    };

    [Fact]
    public void RegisterPatientRequest_AcceptsValidRequest()
    {
        var result = new RegisterPatientRequestValidator().TestValidate(ValidPatientRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    public void RegisterPatientRequest_RejectsWeakPassword(string password)
    {
        var request = ValidPatientRequest();
        request.Password = password;
        request.ConfirmPassword = password;

        var result = new RegisterPatientRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void RegisterPatientRequest_RejectsMismatchedConfirmation()
    {
        var request = ValidPatientRequest();
        request.ConfirmPassword = "Different1";

        var result = new RegisterPatientRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    public void RegisterPatientRequest_RejectsFutureDateOfBirth()
    {
        var request = ValidPatientRequest();
        request.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var result = new RegisterPatientRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DateOfBirth);
    }

    [Fact]
    public void RegisterDoctorRequest_AcceptsValidRequest()
    {
        var result = new RegisterDoctorRequestValidator().TestValidate(ValidDoctorRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void RegisterDoctorRequest_RequiresSpecialization()
    {
        var request = ValidDoctorRequest();
        request.SpecializationId = Guid.Empty;

        var result = new RegisterDoctorRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.SpecializationId);
    }

    [Fact]
    public void RegisterDoctorRequest_RejectsNegativeFee()
    {
        var request = ValidDoctorRequest();
        request.ConsultationFee = -1m;

        var result = new RegisterDoctorRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ConsultationFee);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    public void RegisterDoctorRequest_RejectsUnrealisticExperience(int years)
    {
        var request = ValidDoctorRequest();
        request.YearsOfExperience = years;

        var result = new RegisterDoctorRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.YearsOfExperience);
    }

    [Fact]
    public void LoginRequest_RequiresPassword()
    {
        var result = new LoginRequestValidator().TestValidate(new LoginRequest
        {
            Email = "alice@example.com",
            Password = string.Empty
        });

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void ChangePasswordRequest_RejectsReusingCurrentPassword()
    {
        var result = new ChangePasswordRequestValidator().TestValidate(new ChangePasswordRequest
        {
            CurrentPassword = "Password1",
            NewPassword = "Password1",
            ConfirmPassword = "Password1"
        });

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void CreateSpecializationRequest_RequiresName()
    {
        var result = new CreateSpecializationRequestValidator().TestValidate(new CreateSpecializationRequest
        {
            Name = "   "
        });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SuperAdmin")]
    public void ChangeUserRoleRequest_RejectsUnknownRole(string role)
    {
        var result = new ChangeUserRoleRequestValidator().TestValidate(new ChangeUserRoleRequest
        {
            UserId = Guid.NewGuid(),
            Role = role
        });

        result.ShouldHaveValidationErrorFor(x => x.Role);
    }
}
