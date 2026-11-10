using FluentValidation;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Auth;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.DTOs.Specializations;
using MediCare.Application.DTOs.Users;

namespace MediCare.Application.Validation;

internal static class ValidationRules
{
    public const string PasswordPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$";

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.")
            .EmailAddress().WithMessage("Enter a valid email address.");

    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches(PasswordPattern).WithMessage("Password must contain an uppercase letter, a lowercase letter and a digit.");

    public static IRuleBuilderOptions<T, string> ValidFullName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Full name is required.")
            .MinimumLength(3).WithMessage("Full name must be at least 3 characters.")
            .MaximumLength(150).WithMessage("Full name cannot exceed 150 characters.");
}

public sealed class RegisterPatientRequestValidator : AbstractValidator<RegisterPatientRequest>
{
    public RegisterPatientRequestValidator()
    {
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number cannot exceed 30 characters.")
            .Matches(@"^[0-9+\-() ]*$").WithMessage("Phone number contains invalid characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Please confirm your password.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");
        RuleFor(x => x.DateOfBirth)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.")
            .When(x => x.DateOfBirth.HasValue);
        RuleFor(x => x.Address).MaximumLength(300).WithMessage("Address cannot exceed 300 characters.");
    }
}

public sealed class RegisterDoctorRequestValidator : AbstractValidator<RegisterDoctorRequest>
{
    public RegisterDoctorRequestValidator()
    {
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number cannot exceed 30 characters.")
            .Matches(@"^[0-9+\-() ]*$").WithMessage("Phone number contains invalid characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Please confirm your password.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");
        RuleFor(x => x.SpecializationId)
            .NotEmpty().WithMessage("Select a specialization.");
        RuleFor(x => x.LicenseNumber)
            .NotEmpty().WithMessage("License number is required.")
            .MinimumLength(3).WithMessage("License number must be at least 3 characters.")
            .MaximumLength(60).WithMessage("License number cannot exceed 60 characters.");
        RuleFor(x => x.ConsultationFee)
            .GreaterThanOrEqualTo(0).WithMessage("Consultation fee cannot be negative.")
            .LessThanOrEqualTo(100000).WithMessage("Consultation fee is not realistic.");
        RuleFor(x => x.YearsOfExperience)
            .InclusiveBetween(0, 70).WithMessage("Years of experience must be between 0 and 70.");
        RuleFor(x => x.Bio).MaximumLength(2000).WithMessage("Bio cannot exceed 2000 characters.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Token).NotEmpty().WithMessage("The reset token is missing or invalid.");
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Please confirm your password.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).ValidPassword();
        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Please confirm your new password.")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current password.");
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number cannot exceed 30 characters.")
            .Matches(@"^[0-9+\-() ]*$").WithMessage("Phone number contains invalid characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.DateOfBirth)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.")
            .When(x => x.DateOfBirth.HasValue);
        RuleFor(x => x.Address).MaximumLength(300).WithMessage("Address cannot exceed 300 characters.");
        RuleFor(x => x.EmergencyContact).MaximumLength(150).WithMessage("Emergency contact cannot exceed 150 characters.");
        RuleFor(x => x.BloodType).MaximumLength(10).WithMessage("Blood type cannot exceed 10 characters.");
    }
}

public sealed class UpdateDoctorRequestValidator : AbstractValidator<UpdateDoctorRequest>
{
    public UpdateDoctorRequestValidator()
    {
        RuleFor(x => x.DoctorId).NotEmpty().WithMessage("Doctor is required.");
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number cannot exceed 30 characters.")
            .Matches(@"^[0-9+\-() ]*$").WithMessage("Phone number contains invalid characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.SpecializationId).NotEmpty().WithMessage("Select a specialization.");
        RuleFor(x => x.LicenseNumber)
            .NotEmpty().WithMessage("License number is required.")
            .MinimumLength(3).WithMessage("License number must be at least 3 characters.")
            .MaximumLength(60).WithMessage("License number cannot exceed 60 characters.");
        RuleFor(x => x.ConsultationFee)
            .GreaterThanOrEqualTo(0).WithMessage("Consultation fee cannot be negative.")
            .LessThanOrEqualTo(100000).WithMessage("Consultation fee is not realistic.");
        RuleFor(x => x.YearsOfExperience)
            .InclusiveBetween(0, 70).WithMessage("Years of experience must be between 0 and 70.");
        RuleFor(x => x.Bio).MaximumLength(2000).WithMessage("Bio cannot exceed 2000 characters.");
    }
}

public sealed class CreateSpecializationRequestValidator : AbstractValidator<CreateSpecializationRequest>
{
    public CreateSpecializationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Specialization name is required.")
            .MaximumLength(120).WithMessage("Specialization name cannot exceed 120 characters.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}

public sealed class UpdateSpecializationRequestValidator : AbstractValidator<UpdateSpecializationRequest>
{
    public UpdateSpecializationRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Specialization is required.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Specialization name is required.")
            .MaximumLength(120).WithMessage("Specialization name cannot exceed 120 characters.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}

public sealed class ChangeUserRoleRequestValidator : AbstractValidator<ChangeUserRoleRequest>
{
    public ChangeUserRoleRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User is required.");
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(Roles.IsValid).WithMessage("Select a valid role.");
    }
}
