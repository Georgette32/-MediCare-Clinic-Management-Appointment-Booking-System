using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.DTOs.Patients;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Mappings;
using MediCare.Domain.Exceptions;

namespace MediCare.Application.Services;

public sealed class PatientProfileService : IPatientProfileService
{
    private readonly IPatientRepository _patients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIdentityService _identity;
    private readonly IValidator<UpdatePatientProfileRequest> _updateValidator;

    public PatientProfileService(
        IPatientRepository patients,
        IUnitOfWork unitOfWork,
        IIdentityService identity,
        IValidator<UpdatePatientProfileRequest> updateValidator)
    {
        _patients = patients;
        _unitOfWork = unitOfWork;
        _identity = identity;
        _updateValidator = updateValidator;
    }

    public async Task<Result<PatientProfileDto>> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result<PatientProfileDto>.Fail("A signed-in patient is required.");
        }

        var profile = await _patients.GetByUserIdAsync(userId, false, cancellationToken);
        if (profile is null)
        {
            return Result<PatientProfileDto>.Fail("Patient profile was not found.");
        }

        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        return Result<PatientProfileDto>.Ok(profile.ToDto(user));
    }

    public async Task<Result> UpdateMyProfileAsync(Guid userId, UpdatePatientProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Fail("A signed-in patient is required.");
        }

        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var profile = await _patients.GetByUserIdAsync(userId, true, cancellationToken);
        if (profile is null)
        {
            return Result.Fail("Patient profile was not found.");
        }

        var accountUpdate = await _identity.UpdateAccountAsync(
            userId,
            request.FullName.Trim(),
            string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            cancellationToken);

        if (!accountUpdate.Succeeded)
        {
            return accountUpdate;
        }

        try
        {
            profile.UpdateMedicalInformation(
                request.DateOfBirth,
                request.Gender,
                request.Address,
                request.EmergencyContact,
                request.BloodType);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }

        return Result.Ok();
    }
}
