using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.DTOs.MedicalRecords;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Mappings;
using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Application.Services;

public sealed class MedicalRecordService : IMedicalRecordService
{
    private const string NotFoundMessage = "Medical record was not found.";
    private const string DoctorOnlyMessage = "Only approved doctors can manage medical records.";
    private const string NoAccessMessage = "You do not have access to this patient's records.";

    private readonly IMedicalRecordRepository _records;
    private readonly IDoctorRepository _doctors;
    private readonly IPatientRepository _patients;
    private readonly IIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateMedicalRecordRequest> _createValidator;
    private readonly IValidator<UpdateMedicalRecordRequest> _updateValidator;

    public MedicalRecordService(
        IMedicalRecordRepository records,
        IDoctorRepository doctors,
        IPatientRepository patients,
        IIdentityService identity,
        IUnitOfWork unitOfWork,
        IValidator<CreateMedicalRecordRequest> createValidator,
        IValidator<UpdateMedicalRecordRequest> updateValidator)
    {
        _records = records;
        _doctors = doctors;
        _patients = patients;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<Result<IReadOnlyList<DoctorPatientDto>>> GetMyPatientsAsync(Guid doctorUserId, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<IReadOnlyList<DoctorPatientDto>>.Fail(DoctorOnlyMessage);
        }

        var patients = await _records.GetPatientsWithCompletedAppointmentsAsync(doctor.Id, cancellationToken);
        var users = await _identity.GetUsersByIdsAsync(patients.Select(p => p.UserId).Distinct().ToArray(), cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);

        IReadOnlyList<DoctorPatientDto> items = patients
            .Select(p => new DoctorPatientDto
            {
                PatientProfileId = p.Id,
                FullName = usersById.TryGetValue(p.UserId, out var user) ? user.FullName : "Unknown user",
                Email = usersById.TryGetValue(p.UserId, out var u) ? u.Email : string.Empty
            })
            .ToList();

        return Result<IReadOnlyList<DoctorPatientDto>>.Ok(items);
    }

    public async Task<Result<IReadOnlyList<MedicalRecordDto>>> GetPatientRecordsForDoctorAsync(
        Guid doctorUserId,
        Guid patientProfileId,
        CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<IReadOnlyList<MedicalRecordDto>>.Fail(DoctorOnlyMessage);
        }

        var hasAccess = await _records.DoctorHasCompletedAppointmentWithPatientAsync(
            doctor.Id, patientProfileId, null, cancellationToken);

        if (!hasAccess)
        {
            return Result<IReadOnlyList<MedicalRecordDto>>.Fail(NoAccessMessage);
        }

        var records = await _records.GetByPatientAsync(patientProfileId, cancellationToken);
        IReadOnlyList<MedicalRecordDto> items = await ToDtosAsync(records, cancellationToken);
        return Result<IReadOnlyList<MedicalRecordDto>>.Ok(items);
    }

    public async Task<Result<MedicalRecordDto>> CreateAsync(Guid doctorUserId, CreateMedicalRecordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<MedicalRecordDto>.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<MedicalRecordDto>.Fail(DoctorOnlyMessage);
        }

        var allowed = await _records.DoctorHasCompletedAppointmentWithPatientAsync(
            doctor.Id, request.PatientProfileId, request.AppointmentId, cancellationToken);

        if (!allowed)
        {
            return Result<MedicalRecordDto>.Fail("You can only write records for patients with a completed appointment.");
        }

        MedicalRecord record;
        try
        {
            record = new MedicalRecord(
                request.PatientProfileId,
                doctor.Id,
                request.AppointmentId,
                request.Symptoms,
                request.Diagnosis,
                request.TreatmentPlan,
                request.DoctorNotes);

            await _records.AddAsync(record, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<MedicalRecordDto>.Fail(ex.Message);
        }

        var doctorName = await GetUserNameAsync(doctorUserId, cancellationToken);
        return Result<MedicalRecordDto>.Ok(record.ToDto(doctorName));
    }

    public async Task<Result> UpdateAsync(Guid doctorUserId, UpdateMedicalRecordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail(DoctorOnlyMessage);
        }

        var record = await _records.GetByIdAsync(request.RecordId, true, cancellationToken);
        if (record is null || record.IsDeleted)
        {
            return Result.Fail(NotFoundMessage);
        }

        if (record.DoctorProfileId != doctor.Id)
        {
            return Result.Fail("You can only change records that you wrote.");
        }

        try
        {
            record.Update(request.Symptoms, request.Diagnosis, request.TreatmentPlan, request.DoctorNotes);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Fail(ex.Message);
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Guid doctorUserId, Guid recordId, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail(DoctorOnlyMessage);
        }

        var record = await _records.GetByIdAsync(recordId, true, cancellationToken);
        if (record is null || record.IsDeleted)
        {
            return Result.Fail(NotFoundMessage);
        }

        if (record.DoctorProfileId != doctor.Id)
        {
            return Result.Fail("You can only delete records that you wrote.");
        }

        record.Delete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<IReadOnlyList<MedicalRecordDto>>> GetMyRecordsAsync(Guid patientUserId, CancellationToken cancellationToken = default)
    {
        if (patientUserId == Guid.Empty)
        {
            return Result<IReadOnlyList<MedicalRecordDto>>.Fail("A signed-in patient is required.");
        }

        var patient = await _patients.GetByUserIdAsync(patientUserId, false, cancellationToken);
        if (patient is null)
        {
            return Result<IReadOnlyList<MedicalRecordDto>>.Fail("Patient profile was not found.");
        }

        var records = await _records.GetByPatientAsync(patient.Id, cancellationToken);
        IReadOnlyList<MedicalRecordDto> items = await ToDtosAsync(records, cancellationToken);
        return Result<IReadOnlyList<MedicalRecordDto>>.Ok(items);
    }

    public async Task<Result<MedicalRecordDto>> GetMyRecordAsync(Guid patientUserId, Guid recordId, CancellationToken cancellationToken = default)
    {
        if (patientUserId == Guid.Empty)
        {
            return Result<MedicalRecordDto>.Fail("A signed-in patient is required.");
        }

        var patient = await _patients.GetByUserIdAsync(patientUserId, false, cancellationToken);
        if (patient is null)
        {
            return Result<MedicalRecordDto>.Fail("Patient profile was not found.");
        }

        var record = await _records.GetByIdAsync(recordId, false, cancellationToken);

        // The same message is used for "missing" and "belongs to someone else"
        // so a patient cannot discover which record ids exist.
        if (record is null || record.IsDeleted || record.PatientProfileId != patient.Id)
        {
            return Result<MedicalRecordDto>.Fail(NotFoundMessage);
        }

        var doctorName = await GetDoctorNameAsync(record.DoctorProfileId, cancellationToken);
        return Result<MedicalRecordDto>.Ok(record.ToDto(doctorName));
    }

    private async Task<DoctorProfile?> GetApprovedDoctorAsync(Guid doctorUserId, CancellationToken cancellationToken)
    {
        if (doctorUserId == Guid.Empty)
        {
            return null;
        }

        var doctor = await _doctors.GetByUserIdAsync(doctorUserId, false, cancellationToken);
        return doctor is { IsApproved: true } ? doctor : null;
    }

    private async Task<string> GetUserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        return user?.FullName ?? "Unknown doctor";
    }

    private async Task<string> GetDoctorNameAsync(Guid doctorProfileId, CancellationToken cancellationToken)
    {
        var doctor = await _doctors.GetByIdAsync(doctorProfileId, false, cancellationToken);
        return doctor is null
            ? "Unknown doctor"
            : await GetUserNameAsync(doctor.UserId, cancellationToken);
    }

    private async Task<List<MedicalRecordDto>> ToDtosAsync(IReadOnlyList<MedicalRecord> records, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var doctorId in records.Select(r => r.DoctorProfileId).Distinct())
        {
            names[doctorId] = await GetDoctorNameAsync(doctorId, cancellationToken);
        }

        return records.Select(r => r.ToDto(names[r.DoctorProfileId])).ToList();
    }
}
