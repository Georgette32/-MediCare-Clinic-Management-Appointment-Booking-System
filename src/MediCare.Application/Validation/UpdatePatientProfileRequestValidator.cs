using FluentValidation;
using MediCare.Application.DTOs.Patients;

namespace MediCare.Application.Validation;

public sealed class UpdatePatientProfileRequestValidator : AbstractValidator<UpdatePatientProfileRequest>
{
    private static readonly string[] BloodTypes = { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };

    public UpdatePatientProfileRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(150).WithMessage("Full name cannot exceed 150 characters.");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters.");

        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.")
            .Must(d => d is null || d.Value.Year >= 1900)
            .WithMessage("Date of birth is not valid.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is not valid.");

        RuleFor(x => x.Address)
            .MaximumLength(300).WithMessage("Address cannot exceed 300 characters.");

        RuleFor(x => x.EmergencyContact)
            .MaximumLength(200).WithMessage("Emergency contact cannot exceed 200 characters.");

        RuleFor(x => x.BloodType)
            .Must(b => string.IsNullOrWhiteSpace(b) || BloodTypes.Contains(b.Trim().ToUpperInvariant()))
            .WithMessage("Blood type is not valid. Use A+, A-, B+, B-, AB+, AB-, O+ or O-.");
    }
}
