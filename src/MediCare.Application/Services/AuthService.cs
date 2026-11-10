using System.Net;
using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Auth;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MediCare.Application.Services;

public sealed class AuthService : IAuthService
{
    private readonly IIdentityService _identity;
    private readonly IPatientRepository _patients;
    private readonly IDoctorRepository _doctors;
    private readonly ISpecializationRepository _specializations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _email;
    private readonly IAppUrlProvider _urls;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<RegisterPatientRequest> _patientValidator;
    private readonly IValidator<RegisterDoctorRequest> _doctorValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotValidator;
    private readonly IValidator<ResetPasswordRequest> _resetValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
    private readonly IValidator<UpdateProfileRequest> _profileValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IIdentityService identity,
        IPatientRepository patients,
        IDoctorRepository doctors,
        ISpecializationRepository specializations,
        IUnitOfWork unitOfWork,
        IEmailService email,
        IAppUrlProvider urls,
        ICurrentUserService currentUser,
        IValidator<RegisterPatientRequest> patientValidator,
        IValidator<RegisterDoctorRequest> doctorValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<ForgotPasswordRequest> forgotValidator,
        IValidator<ResetPasswordRequest> resetValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator,
        IValidator<UpdateProfileRequest> profileValidator,
        ILogger<AuthService> logger)
    {
        _identity = identity;
        _patients = patients;
        _doctors = doctors;
        _specializations = specializations;
        _unitOfWork = unitOfWork;
        _email = email;
        _urls = urls;
        _currentUser = currentUser;
        _patientValidator = patientValidator;
        _doctorValidator = doctorValidator;
        _loginValidator = loginValidator;
        _forgotValidator = forgotValidator;
        _resetValidator = resetValidator;
        _changePasswordValidator = changePasswordValidator;
        _profileValidator = profileValidator;
        _logger = logger;
    }

    public bool RequiresEmailConfirmation => _identity.RequiresEmailConfirmation;

    public async Task<Result> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _patientValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var email = request.Email.Trim();
        if (await _identity.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return Result.Fail("An account with this email already exists.");
        }

        var created = await _identity.CreateUserAsync(
            email,
            request.Password,
            request.FullName.Trim(),
            Normalize(request.PhoneNumber),
            Roles.Patient,
            cancellationToken);

        if (!created.Succeeded || created.Value == Guid.Empty)
        {
            return Result.Fail(created.Errors);
        }

        var userId = created.Value;

        try
        {
            var profile = new PatientProfile(userId);
            profile.UpdateMedicalInformation(
                request.DateOfBirth,
                request.Gender,
                request.Address,
                null,
                null);

            await _patients.AddAsync(profile, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            await _identity.DeleteUserAsync(userId, cancellationToken);
            return Result.Fail(ex.Message);
        }
        catch
        {
            await _identity.DeleteUserAsync(userId, cancellationToken);
            throw;
        }

        await TrySendConfirmationEmailAsync(userId, email, request.FullName.Trim(), cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> RegisterDoctorAsync(RegisterDoctorRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _doctorValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var specialization = await _specializations.GetByIdAsync(request.SpecializationId, false, cancellationToken);
        if (specialization is null || !specialization.IsActive)
        {
            return Result.Fail("Select an active specialization.");
        }

        if (await _doctors.LicenseNumberExistsAsync(request.LicenseNumber, null, cancellationToken))
        {
            return Result.Fail("A doctor with this license number is already registered.");
        }

        var email = request.Email.Trim();
        if (await _identity.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return Result.Fail("An account with this email already exists.");
        }

        var created = await _identity.CreateUserAsync(
            email,
            request.Password,
            request.FullName.Trim(),
            Normalize(request.PhoneNumber),
            Roles.Doctor,
            cancellationToken);

        if (!created.Succeeded || created.Value == Guid.Empty)
        {
            return Result.Fail(created.Errors);
        }

        var userId = created.Value;

        try
        {
            var profile = new DoctorProfile(
                userId,
                request.SpecializationId,
                request.LicenseNumber,
                request.ConsultationFee,
                request.YearsOfExperience,
                request.Bio);

            await _doctors.AddAsync(profile, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            await _identity.DeleteUserAsync(userId, cancellationToken);
            return Result.Fail(ex.Message);
        }
        catch
        {
            await _identity.DeleteUserAsync(userId, cancellationToken);
            throw;
        }

        await TrySendConfirmationEmailAsync(userId, email, request.FullName.Trim(), cancellationToken);
        return Result.Ok();
    }

    public async Task<SignInOutcome> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _loginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return SignInOutcome.InvalidCredentials;
        }

        return await _identity.PasswordSignInAsync(
            request.Email.Trim(),
            request.Password,
            request.RememberMe,
            cancellationToken);
    }

    public Task LogoutAsync() => _identity.SignOutAsync();

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _forgotValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        // Always report success to avoid disclosing whether an account exists.
        var user = await _identity.FindByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Ok();
        }

        var token = await _identity.GeneratePasswordResetTokenAsync(user.Email, cancellationToken);
        if (!token.Succeeded || string.IsNullOrWhiteSpace(token.Value))
        {
            _logger.LogWarning("Could not generate a password reset token for {Email}.", user.Email);
            return Result.Ok();
        }

        var link = _urls.PasswordResetUrl(user.Id, token.Value);
        var body = $"""
            <p>Hello {WebUtility.HtmlEncode(user.FullName)},</p>
            <p>We received a request to reset your MediCare password.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Reset your password</a></p>
            <p>If you did not request this, you can safely ignore this email.</p>
            """;

        await TrySendEmailAsync(user.Email, "Reset your MediCare password", body, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _resetValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var reset = await _identity.ResetPasswordAsync(
            request.Email.Trim(),
            request.Token,
            request.Password,
            cancellationToken);

        return reset.Succeeded
            ? Result.Ok()
            : Result.Fail("The reset link is invalid or has expired. Please request a new one.");
    }

    public async Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return Result.Fail("The confirmation link is invalid.");
        }

        return await _identity.ConfirmEmailAsync(userId, token, cancellationToken);
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Result.Fail("You must be signed in to change your password.");
        }

        var validation = await _changePasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        return await _identity.ChangePasswordAsync(userId.Value, request.CurrentPassword, request.NewPassword, cancellationToken);
    }

    public async Task<Result<ProfileDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<ProfileDto>.Fail("User was not found.");
        }

        var roles = await _identity.GetRolesAsync(userId, cancellationToken);
        var profile = new ProfileDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            Roles = roles
        };

        if (roles.Contains(Roles.Patient, StringComparer.OrdinalIgnoreCase))
        {
            var patient = await _patients.GetByUserIdAsync(userId, false, cancellationToken);
            if (patient is not null)
            {
                profile.DateOfBirth = patient.DateOfBirth;
                profile.Gender = patient.Gender;
                profile.Address = patient.Address;
                profile.EmergencyContact = patient.EmergencyContact;
                profile.BloodType = patient.BloodType;
            }
        }

        return Result<ProfileDto>.Ok(profile);
    }

    public async Task<Result> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _profileValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<ProfileDto>.Fail("User was not found.");
        }

        var updated = await _identity.UpdateAccountAsync(
            userId,
            request.FullName.Trim(),
            Normalize(request.PhoneNumber),
            cancellationToken);

        if (!updated.Succeeded)
        {
            return updated;
        }

        var roles = await _identity.GetRolesAsync(userId, cancellationToken);
        if (roles.Contains(Roles.Patient, StringComparer.OrdinalIgnoreCase))
        {
            var patient = await _patients.GetByUserIdAsync(userId, true, cancellationToken);
            if (patient is not null)
            {
                try
                {
                    patient.UpdateMedicalInformation(
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
            }
        }

        return Result.Ok();
    }

    private async Task TrySendConfirmationEmailAsync(Guid userId, string email, string fullName, CancellationToken cancellationToken)
    {
        if (!_identity.RequiresEmailConfirmation)
        {
            return;
        }

        var token = await _identity.GenerateEmailConfirmationTokenAsync(userId, cancellationToken);
        if (!token.Succeeded || string.IsNullOrWhiteSpace(token.Value))
        {
            _logger.LogWarning("Could not generate an email confirmation token for {Email}.", email);
            return;
        }

        var link = _urls.EmailConfirmationUrl(userId, token.Value);
        var body = $"""
            <p>Hello {WebUtility.HtmlEncode(fullName)},</p>
            <p>Welcome to MediCare. Please confirm your email address to activate your account.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Confirm my email</a></p>
            <p>If you did not create this account, please ignore this email.</p>
            """;

        await TrySendEmailAsync(email, "Confirm your MediCare account", body, cancellationToken);
    }

    private async Task TrySendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await _email.SendAsync(to, subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            // Registration must not fail because the mail server is temporarily down.
            _logger.LogError(ex, "Failed to send an email to {Email}.", to);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
