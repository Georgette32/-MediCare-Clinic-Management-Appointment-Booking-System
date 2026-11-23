using FluentValidation;
using MediCare.Application.DTOs.MedicalRecords;

namespace MediCare.Application.Validation;

public sealed class CreateMedicalRecordRequestValidator : AbstractValidator<CreateMedicalRecordRequest>
{
    public CreateMedicalRecordRequestValidator()
    {
        RuleFor(x => x.PatientProfileId)
            .NotEmpty().WithMessage("A patient is required.");

        RuleFor(x => x.AppointmentId)
            .Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("The appointment is not valid.");

        RuleFor(x => x.Symptoms)
            .NotEmpty().WithMessage("Symptoms are required.")
            .MaximumLength(2000).WithMessage("Symptoms cannot exceed 2000 characters.");

        RuleFor(x => x.Diagnosis)
            .NotEmpty().WithMessage("A diagnosis is required.")
            .MaximumLength(2000).WithMessage("The diagnosis cannot exceed 2000 characters.");

        RuleFor(x => x.TreatmentPlan)
            .MaximumLength(2000).WithMessage("The treatment plan cannot exceed 2000 characters.");

        RuleFor(x => x.DoctorNotes)
            .MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");
    }
}

public sealed class UpdateMedicalRecordRequestValidator : AbstractValidator<UpdateMedicalRecordRequest>
{
    public UpdateMedicalRecordRequestValidator()
    {
        RuleFor(x => x.RecordId)
            .NotEmpty().WithMessage("The medical record is required.");

        RuleFor(x => x.Symptoms)
            .NotEmpty().WithMessage("Symptoms are required.")
            .MaximumLength(2000).WithMessage("Symptoms cannot exceed 2000 characters.");

        RuleFor(x => x.Diagnosis)
            .NotEmpty().WithMessage("A diagnosis is required.")
            .MaximumLength(2000).WithMessage("The diagnosis cannot exceed 2000 characters.");

        RuleFor(x => x.TreatmentPlan)
            .MaximumLength(2000).WithMessage("The treatment plan cannot exceed 2000 characters.");

        RuleFor(x => x.DoctorNotes)
            .MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");
    }
}
