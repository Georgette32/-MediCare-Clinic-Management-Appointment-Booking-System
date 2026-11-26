using System.Data;
using System.Security.Cryptography;
using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

public interface IAccountProvisioningService
{
    Task<IReadOnlyList<SpecializationChoice>> GetSpecializationsAsync(CancellationToken cancellationToken);
    Task<AccountProvisioningResult> CreateDoctorAsync(CreateDoctorAccountRequest request, CancellationToken cancellationToken);
    Task<AccountProvisioningResult> CreateReceptionistAsync(CreateStaffAccountRequest request, CancellationToken cancellationToken);
    Task<AccountProvisioningResult> CreatePatientAsync(CreatePatientAccountRequest request, CancellationToken cancellationToken);
    Task<AccountProvisioningResult> RegisterPatientAsync(RegisterPatientAccountRequest request, CancellationToken cancellationToken);
}

public sealed record CreateDoctorAccountRequest(string Email, string FullName, Guid SpecializationId, int AppointmentDurationMinutes);
public sealed record CreateStaffAccountRequest(string Email, string FullName);
public sealed record CreatePatientAccountRequest(string PhoneNumber, string FullName);
public sealed record RegisterPatientAccountRequest(string PhoneNumber, string FullName, string? Email, string Password);
public sealed record AccountProvisioningResult(bool Succeeded, string? TemporaryPassword, string? CanonicalPhone, IReadOnlyList<string> Errors)
{
    public static AccountProvisioningResult Success(string? temporaryPassword = null, string? canonicalPhone = null) =>
        new(true, temporaryPassword, canonicalPhone, []);

    public static AccountProvisioningResult Failure(params string[] errors) =>
        new(false, null, null, errors);
}

public sealed class AccountProvisioningConflictException(string message) : Exception(message);

public sealed class AccountProvisioningService(
    IIdentityAccountStore identity,
    IAccountProvisioningRepository accounts,
    IUnitOfWork unitOfWork) : IAccountProvisioningService
{
    private const string DoctorRole = "Doctor";
    private const string ReceptionistRole = "Receptionist";
    private const string PatientRole = "Patient";

    public Task<IReadOnlyList<SpecializationChoice>> GetSpecializationsAsync(CancellationToken cancellationToken) =>
        accounts.GetSpecializationsAsync(cancellationToken);

    public async Task<AccountProvisioningResult> CreateDoctorAsync(CreateDoctorAccountRequest request, CancellationToken cancellationToken)
    {
        if (request.AppointmentDurationMinutes <= 0)
            return AccountProvisioningResult.Failure("Appointment duration must be a positive number of minutes.");
        if (request.SpecializationId == Guid.Empty || !await accounts.SpecializationExistsAsync(request.SpecializationId, cancellationToken))
            return AccountProvisioningResult.Failure("Select an existing specialization.");

        var email = NormalizeEmail(request.Email);
        if (await identity.EmailExistsAsync(email, cancellationToken))
            return AccountProvisioningResult.Failure("An account with this email already exists.");

        var temporaryPassword = TemporaryPasswordGenerator.Create();
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var created = await identity.CreateAsync(email, email, request.FullName.Trim(), null, DoctorRole, temporaryPassword, true, false, cancellationToken);
        if (!created.Succeeded)
            return AccountProvisioningResult.Failure(created.Errors.ToArray());

        await accounts.AddDoctorAsync(new Doctor(Guid.NewGuid(), created.UserId, request.FullName.Trim(), request.SpecializationId, request.AppointmentDurationMinutes), cancellationToken);
        try
        {
            await accounts.SaveChangesAsync(cancellationToken);
        }
        catch (AccountProvisioningConflictException exception)
        {
            return AccountProvisioningResult.Failure(exception.Message);
        }
        await transaction.CommitAsync(cancellationToken);
        return AccountProvisioningResult.Success(temporaryPassword);
    }

    public async Task<AccountProvisioningResult> CreateReceptionistAsync(CreateStaffAccountRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (await identity.EmailExistsAsync(email, cancellationToken))
            return AccountProvisioningResult.Failure("An account with this email already exists.");

        var temporaryPassword = TemporaryPasswordGenerator.Create();
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var created = await identity.CreateAsync(email, email, request.FullName.Trim(), null, ReceptionistRole, temporaryPassword, true, false, cancellationToken);
        if (!created.Succeeded)
            return AccountProvisioningResult.Failure(created.Errors.ToArray());
        await transaction.CommitAsync(cancellationToken);
        return AccountProvisioningResult.Success(temporaryPassword);
    }

    public async Task<AccountProvisioningResult> CreatePatientAsync(CreatePatientAccountRequest request, CancellationToken cancellationToken)
    {
        if (!EgyptianMobileNumber.TryNormalize(request.PhoneNumber, out var phone, out var error))
            return AccountProvisioningResult.Failure(error);
        if (await accounts.PatientPhoneExistsAsync(phone, cancellationToken))
            return AccountProvisioningResult.Failure("A patient account already uses this phone number.");

        var temporaryPassword = TemporaryPasswordGenerator.Create();
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var created = await identity.CreateAsync(phone, null, request.FullName.Trim(), phone, PatientRole, temporaryPassword, true, false, cancellationToken);
        if (!created.Succeeded)
            return AccountProvisioningResult.Failure(created.Errors.ToArray());

        await accounts.AddPatientAsync(new Patient(Guid.NewGuid(), created.UserId, request.FullName.Trim(), phone), cancellationToken);
        try
        {
            await accounts.SaveChangesAsync(cancellationToken);
        }
        catch (AccountProvisioningConflictException exception)
        {
            return AccountProvisioningResult.Failure(exception.Message);
        }
        await transaction.CommitAsync(cancellationToken);
        return AccountProvisioningResult.Success(temporaryPassword, phone);
    }

    public async Task<AccountProvisioningResult> RegisterPatientAsync(RegisterPatientAccountRequest request, CancellationToken cancellationToken)
    {
        if (!EgyptianMobileNumber.TryNormalize(request.PhoneNumber, out var phone, out var error))
            return AccountProvisioningResult.Failure(error);
        if (await accounts.PatientPhoneExistsAsync(phone, cancellationToken))
            return AccountProvisioningResult.Failure("A patient account already uses this phone number.");

        // Self-registered patients confirm their email with a code before the account can be used.
        if (string.IsNullOrWhiteSpace(request.Email))
            return AccountProvisioningResult.Failure("Enter your email address. We'll send a code to it to confirm your account.");
        var email = NormalizeEmail(request.Email);
        if (await identity.EmailExistsAsync(email, cancellationToken))
            return AccountProvisioningResult.Failure("An account with this email already exists.");

        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var created = await identity.CreateAsync(phone, email, request.FullName.Trim(), phone, PatientRole, request.Password, false, true, cancellationToken);
        if (!created.Succeeded)
            return AccountProvisioningResult.Failure(created.Errors.ToArray());

        await accounts.AddPatientAsync(new Patient(Guid.NewGuid(), created.UserId, request.FullName.Trim(), phone), cancellationToken);
        try
        {
            await accounts.SaveChangesAsync(cancellationToken);
        }
        catch (AccountProvisioningConflictException exception)
        {
            return AccountProvisioningResult.Failure(exception.Message);
        }
        await transaction.CommitAsync(cancellationToken);
        return AccountProvisioningResult.Success(canonicalPhone: phone);
    }

    private static string NormalizeEmail(string email) => email.Trim();
}

public static class EgyptianMobileNumber
{
    public static bool TryNormalize(string? input, out string canonical, out string error)
    {
        canonical = string.Empty;
        error = "Enter a valid Egyptian mobile number.";
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var digits = new System.Text.StringBuilder(input.Length);
        foreach (var character in input)
        {
            if (character is ' ' or '-' or '(' or ')')
                continue;
            if (character is < '0' or > '9')
            {
                if (character == '+' && digits.Length == 0)
                {
                    digits.Append('+');
                    continue;
                }
                return false;
            }
            digits.Append(character);
        }

        var value = digits.ToString();
        string national;
        if (value.StartsWith("+20", StringComparison.Ordinal))
            national = value[3..];
        else if (value.StartsWith("0020", StringComparison.Ordinal))
            national = value[4..];
        else if (value.Length == 11 && value.StartsWith("01", StringComparison.Ordinal))
            national = value[1..];
        else
            return false;

        if (national.Length != 10 || national[0] != '1' || national[1] is not ('0' or '1' or '2' or '5') || national.Any(character => character is < '0' or > '9'))
            return false;

        canonical = "+20" + national;
        error = string.Empty;
        return true;
    }
}

internal static class TemporaryPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@$%*-_";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Create()
    {
        var password = new char[24];
        password[0] = Pick(Upper);
        password[1] = Pick(Lower);
        password[2] = Pick(Digits);
        password[3] = Pick(Symbols);
        for (var index = 4; index < password.Length; index++)
            password[index] = Pick(All);

        for (var index = password.Length - 1; index > 0; index--)
        {
            var other = RandomNumberGenerator.GetInt32(index + 1);
            (password[index], password[other]) = (password[other], password[index]);
        }
        return new string(password);
    }

    private static char Pick(string values) => values[RandomNumberGenerator.GetInt32(values.Length)];
}
