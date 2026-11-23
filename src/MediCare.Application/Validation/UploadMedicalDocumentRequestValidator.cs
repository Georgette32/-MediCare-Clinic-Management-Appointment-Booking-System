using FluentValidation;
using MediCare.Application.DTOs.Documents;

namespace MediCare.Application.Validation;

public sealed class UploadMedicalDocumentRequestValidator : AbstractValidator<UploadMedicalDocumentRequest>
{
    public UploadMedicalDocumentRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Choose a file to upload.")
            .MaximumLength(MedicalDocumentRules.MaxFileNameLength)
            .WithMessage("The file name is too long.");

        RuleFor(x => x.FileName)
            .Must(name => MedicalDocumentRules.IsAllowedExtension(Path.GetExtension(name)))
            .When(x => !string.IsNullOrWhiteSpace(x.FileName))
            .WithMessage("Only PDF, PNG and JPG files are allowed.");

        RuleFor(x => x.SizeInBytes)
            .GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(MedicalDocumentRules.MaxSizeInBytes)
            .WithMessage("The file cannot be larger than 5 MB.");

        RuleFor(x => x.Content)
            .NotNull().WithMessage("Choose a file to upload.");
    }
}
