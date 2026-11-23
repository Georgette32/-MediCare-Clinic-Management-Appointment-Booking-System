using FluentValidation;
using MediCare.Application.DTOs.Prescriptions;

namespace MediCare.Application.Validation;

public sealed class CreatePrescriptionRequestValidator : AbstractValidator<CreatePrescriptionRequest>
{
    public CreatePrescriptionRequestValidator()
    {
        RuleFor(x => x.PatientProfileId)
            .NotEmpty().WithMessage("A patient is required.");

        RuleFor(x => x.MedicalRecordId)
            .Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("The medical record is not valid.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Add at least one medication.");

        RuleFor(x => x.Items)
            .Must(items => items is null || items.Count <= 20)
            .WithMessage("A prescription cannot contain more than 20 medications.");

        RuleForEach(x => x.Items)
            .SetValidator(new PrescriptionItemRequestValidator());
    }
}

public sealed class PrescriptionItemRequestValidator : AbstractValidator<PrescriptionItemRequest>
{
    public PrescriptionItemRequestValidator()
    {
        RuleFor(x => x.MedicationName)
            .NotEmpty().WithMessage("The medication name is required.")
            .MaximumLength(200).WithMessage("The medication name cannot exceed 200 characters.");

        RuleFor(x => x.Dosage)
            .NotEmpty().WithMessage("The dosage is required.")
            .MaximumLength(100).WithMessage("The dosage cannot exceed 100 characters.");

        RuleFor(x => x.Frequency)
            .NotEmpty().WithMessage("The frequency is required.")
            .MaximumLength(100).WithMessage("The frequency cannot exceed 100 characters.");

        RuleFor(x => x.DurationInDays)
            .InclusiveBetween(1, 365).WithMessage("The duration must be between 1 and 365 days.");

        RuleFor(x => x.Instructions)
            .MaximumLength(500).WithMessage("Instructions cannot exceed 500 characters.");
    }
}
