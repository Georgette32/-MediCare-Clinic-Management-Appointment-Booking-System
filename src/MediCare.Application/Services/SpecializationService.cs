using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.DTOs.Specializations;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Application.Services;

public sealed class SpecializationService : ISpecializationService
{
    private readonly ISpecializationRepository _specializations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateSpecializationRequest> _createValidator;
    private readonly IValidator<UpdateSpecializationRequest> _updateValidator;

    public SpecializationService(
        ISpecializationRepository specializations,
        IUnitOfWork unitOfWork,
        IValidator<CreateSpecializationRequest> createValidator,
        IValidator<UpdateSpecializationRequest> updateValidator)
    {
        _specializations = specializations;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<SpecializationDto>> GetAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var items = await _specializations.GetAllAsync(includeInactive, cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<SpecializationDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var items = await _specializations.GetActiveAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<Result<SpecializationDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var specialization = await _specializations.GetByIdAsync(id, false, cancellationToken);
        return specialization is null
            ? Result<SpecializationDto>.Fail("Specialization was not found.")
            : Result<SpecializationDto>.Ok(ToDto(specialization));
    }

    public async Task<Result<SpecializationDto>> CreateAsync(CreateSpecializationRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<SpecializationDto>.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        if (await _specializations.NameExistsAsync(request.Name, null, cancellationToken))
        {
            return Result<SpecializationDto>.Fail("A specialization with this name already exists.");
        }

        try
        {
            var specialization = new Specialization(request.Name, request.Description);
            await _specializations.AddAsync(specialization, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<SpecializationDto>.Ok(ToDto(specialization));
        }
        catch (DomainException ex)
        {
            return Result<SpecializationDto>.Fail(ex.Message);
        }
    }

    public async Task<Result> UpdateAsync(UpdateSpecializationRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var specialization = await _specializations.GetByIdAsync(request.Id, true, cancellationToken);
        if (specialization is null)
        {
            return Result.Fail("Specialization was not found.");
        }

        if (await _specializations.NameExistsAsync(request.Name, request.Id, cancellationToken))
        {
            return Result.Fail("A specialization with this name already exists.");
        }

        try
        {
            specialization.Update(request.Name, request.Description);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var specialization = await _specializations.GetByIdAsync(id, true, cancellationToken);
        if (specialization is null)
        {
            return Result.Fail("Specialization was not found.");
        }

        if (isActive)
        {
            specialization.Activate();
        }
        else
        {
            specialization.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var specialization = await _specializations.GetByIdAsync(id, true, cancellationToken);
        if (specialization is null)
        {
            return Result.Fail("Specialization was not found.");
        }

        if (await _specializations.CountDoctorsAsync(id, cancellationToken) > 0)
        {
            return Result.Fail("This specialization is assigned to doctors. Deactivate it instead of deleting it.");
        }

        _specializations.Remove(specialization);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    private static SpecializationDto ToDto(Specialization specialization) => new()
    {
        Id = specialization.Id,
        Name = specialization.Name,
        Description = specialization.Description,
        IsActive = specialization.IsActive,
        DoctorCount = specialization.Doctors.Count
    };
}
