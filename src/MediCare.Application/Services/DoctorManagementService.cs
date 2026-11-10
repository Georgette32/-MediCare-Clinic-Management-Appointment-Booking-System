using System.Net;
using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Mappings;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MediCare.Application.Services;

public sealed class DoctorManagementService : IDoctorManagementService
{
    private readonly IDoctorRepository _doctors;
    private readonly ISpecializationRepository _specializations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIdentityService _identity;
    private readonly IEmailService _email;
    private readonly IAppUrlProvider _urls;
    private readonly IValidator<UpdateDoctorRequest> _updateValidator;
    private readonly ILogger<DoctorManagementService> _logger;

    public DoctorManagementService(
        IDoctorRepository doctors,
        ISpecializationRepository specializations,
        IUnitOfWork unitOfWork,
        IIdentityService identity,
        IEmailService email,
        IAppUrlProvider urls,
        IValidator<UpdateDoctorRequest> updateValidator,
        ILogger<DoctorManagementService> logger)
    {
        _doctors = doctors;
        _specializations = specializations;
        _unitOfWork = unitOfWork;
        _identity = identity;
        _email = email;
        _urls = urls;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    public async Task<PagedResult<DoctorListItemDto>> GetDoctorsAsync(DoctorQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _doctors.GetPagedAsync(query, cancellationToken);
        var users = await LoadUsersAsync(page.Items.Select(d => d.UserId), cancellationToken);

        var items = page.Items
            .Select(d => d.ToListItemDto(users.GetValueOrDefault(d.UserId)))
            .ToList();

        return new PagedResult<DoctorListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<IReadOnlyList<DoctorListItemDto>> GetPendingDoctorsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _doctors.GetPendingAsync(cancellationToken);
        var users = await LoadUsersAsync(pending.Select(d => d.UserId), cancellationToken);

        return pending
            .Select(d => d.ToListItemDto(users.GetValueOrDefault(d.UserId)))
            .ToList();
    }

    public async Task<Result<DoctorDetailsDto>> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var doctor = await _doctors.GetByIdAsync(doctorId, false, cancellationToken);
        if (doctor is null)
        {
            return Result<DoctorDetailsDto>.Fail("Doctor was not found.");
        }

        var user = await _identity.FindByIdAsync(doctor.UserId, cancellationToken);
        return Result<DoctorDetailsDto>.Ok(doctor.ToDetailsDto(user));
    }

    public async Task<Result<DoctorDetailsDto>> GetDoctorByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var doctor = await _doctors.GetByUserIdAsync(userId, false, cancellationToken);
        if (doctor is null)
        {
            return Result<DoctorDetailsDto>.Fail("Doctor profile was not found.");
        }

        var user = await _identity.FindByIdAsync(doctor.UserId, cancellationToken);
        return Result<DoctorDetailsDto>.Ok(doctor.ToDetailsDto(user));
    }

    public async Task<Result> ApproveAsync(Guid administratorId, Guid doctorId, CancellationToken cancellationToken = default)
    {
        if (administratorId == Guid.Empty)
        {
            return Result.Fail("An administrator is required.");
        }

        var doctor = await _doctors.GetByIdAsync(doctorId, true, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail("Doctor was not found.");
        }

        if (doctor.IsApproved)
        {
            return Result.Ok();
        }

        try
        {
            doctor.Approve(administratorId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }

        await NotifyDoctorAsync(doctor.UserId, "Your MediCare application was approved",
            "Your doctor account has been approved. You can now sign in and access the doctor dashboard.",
            cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> RejectAsync(Guid administratorId, Guid doctorId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail("A rejection reason is required.");
        }

        var doctor = await _doctors.GetByIdAsync(doctorId, true, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail("Doctor was not found.");
        }

        try
        {
            doctor.Reject(administratorId, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }

        await NotifyDoctorAsync(doctor.UserId, "Your MediCare application was not approved",
            $"Your doctor application was rejected. Reason: {WebUtility.HtmlEncode(reason.Trim())}",
            cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> SuspendAsync(Guid administratorId, Guid doctorId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail("A suspension reason is required.");
        }

        var doctor = await _doctors.GetByIdAsync(doctorId, true, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail("Doctor was not found.");
        }

        try
        {
            doctor.Suspend(administratorId, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }

        await NotifyDoctorAsync(doctor.UserId, "Your MediCare doctor access was suspended",
            $"Your doctor access was suspended. Reason: {WebUtility.HtmlEncode(reason.Trim())}",
            cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> SetAccountActiveAsync(Guid administratorId, Guid doctorId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (administratorId == Guid.Empty)
        {
            return Result.Fail("An administrator is required.");
        }

        var doctor = await _doctors.GetByIdAsync(doctorId, false, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail("Doctor was not found.");
        }

        if (doctor.UserId == administratorId && !isActive)
        {
            return Result.Fail("You cannot deactivate your own account.");
        }

        var result = await _identity.SetActiveAsync(doctor.UserId, isActive, cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        await NotifyDoctorAsync(
            doctor.UserId,
            isActive ? "Your MediCare account was activated" : "Your MediCare account was deactivated",
            isActive
                ? "Your account has been activated. You can sign in again."
                : "Your account has been deactivated. Contact the clinic administration for assistance.",
            cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> UpdateAsync(Guid administratorId, UpdateDoctorRequest request, CancellationToken cancellationToken = default)
    {
        if (administratorId == Guid.Empty)
        {
            return Result.Fail("An administrator is required.");
        }

        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var doctor = await _doctors.GetByIdAsync(request.DoctorId, true, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail("Doctor was not found.");
        }

        var specialization = await _specializations.GetByIdAsync(request.SpecializationId, false, cancellationToken);
        if (specialization is null)
        {
            return Result.Fail("The selected specialization does not exist.");
        }

        if (!specialization.IsActive && specialization.Id != doctor.SpecializationId)
        {
            return Result.Fail("The selected specialization is inactive.");
        }

        if (await _doctors.LicenseNumberExistsAsync(request.LicenseNumber, doctor.Id, cancellationToken))
        {
            return Result.Fail("A doctor with this license number is already registered.");
        }

        var accountUpdate = await _identity.UpdateAccountAsync(
            doctor.UserId,
            request.FullName.Trim(),
            string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            cancellationToken);

        if (!accountUpdate.Succeeded)
        {
            return accountUpdate;
        }

        try
        {
            doctor.UpdateProfessionalDetails(
                request.SpecializationId,
                request.LicenseNumber,
                request.ConsultationFee,
                request.YearsOfExperience,
                request.Bio);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }

        return Result.Ok();
    }

    private async Task<Dictionary<Guid, IdentityUserInfo>> LoadUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, IdentityUserInfo>();
        }

        var users = await _identity.GetUsersByIdsAsync(ids, cancellationToken);
        return users.ToDictionary(u => u.Id);
    }

    private async Task NotifyDoctorAsync(Guid userId, string subject, string message, CancellationToken cancellationToken)
    {
        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var body = $"""
            <p>Hello {WebUtility.HtmlEncode(user.FullName)},</p>
            <p>{message}</p>
            <p><a href="{WebUtility.HtmlEncode(_urls.LoginUrl())}">Open MediCare</a></p>
            """;

        try
        {
            await _email.SendAsync(user.Email, subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send a doctor notification to {Email}.", user.Email);
        }
    }
}
