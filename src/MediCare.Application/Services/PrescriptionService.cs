using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.DTOs.Prescriptions;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Mappings;
using MediCare.Domain.Entities;
using MediCare.Domain.Exceptions;

namespace MediCare.Application.Services;

public sealed class PrescriptionService : IPrescriptionService
{
    private const string NotFoundMessage = "Prescription was not found.";
    private const string DoctorOnlyMessage = "Only approved doctors can manage prescriptions.";
    private const string NoAccessMessage = "You do not have access to this patient's prescriptions.";

    private readonly IPrescriptionRepository _prescriptions;
    private readonly IMedicalRecordRepository _medicalRecords;
    private readonly IDoctorRepository _doctors;
    private readonly IPatientRepository _patients;
    private readonly IIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreatePrescriptionRequest> _createValidator;

    public PrescriptionService(
        IPrescriptionRepository prescriptions,
        IMedicalRecordRepository medicalRecords,
        IDoctorRepository doctors,
        IPatientRepository patients,
        IIdentityService identity,
        IUnitOfWork unitOfWork,
        IValidator<CreatePrescriptionRequest> createValidator)
    {
        _prescriptions = prescriptions;
        _medicalRecords = medicalRecords;
        _doctors = doctors;
        _patients = patients;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
    }

    public async Task<Result<PrescriptionDto>> CreateAsync(Guid doctorUserId, CreatePrescriptionRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<PrescriptionDto>.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<PrescriptionDto>.Fail(DoctorOnlyMessage);
        }

        var allowed = await _medicalRecords.DoctorHasCompletedAppointmentWithPatientAsync(
            doctor.Id, request.PatientProfileId, null, cancellationToken);

        if (!allowed)
        {
            return Result<PrescriptionDto>.Fail("You can only write prescriptions for patients with a completed appointment.");
        }

        if (request.MedicalRecordId.HasValue)
        {
            var record = await _medicalRecords.GetByIdAsync(request.MedicalRecordId.Value, false, cancellationToken);
            if (record is null || record.IsDeleted || record.PatientProfileId != request.PatientProfileId)
            {
                return Result<PrescriptionDto>.Fail("The selected medical record does not belong to this patient.");
            }
        }

        Prescription prescription;
        try
        {
            prescription = new Prescription(
                request.PatientProfileId,
                doctor.Id,
                request.MedicalRecordId,
                request.Notes);

            foreach (var item in request.Items)
            {
                prescription.AddItem(
                    item.MedicationName,
                    item.Dosage,
                    item.Frequency,
                    item.DurationInDays,
                    item.Instructions);
            }

            await _prescriptions.AddAsync(prescription, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<PrescriptionDto>.Fail(ex.Message);
        }

        var doctorName = await GetUserNameAsync(doctorUserId, cancellationToken);
        return Result<PrescriptionDto>.Ok(prescription.ToDto(doctorName));
    }

    public async Task<Result<IReadOnlyList<PrescriptionDto>>> GetPatientPrescriptionsForDoctorAsync(
        Guid doctorUserId,
        Guid patientProfileId,
        CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result<IReadOnlyList<PrescriptionDto>>.Fail(DoctorOnlyMessage);
        }

        var hasAccess = await _medicalRecords.DoctorHasCompletedAppointmentWithPatientAsync(
            doctor.Id, patientProfileId, null, cancellationToken);

        if (!hasAccess)
        {
            return Result<IReadOnlyList<PrescriptionDto>>.Fail(NoAccessMessage);
        }

        var prescriptions = await _prescriptions.GetByPatientAsync(patientProfileId, cancellationToken);
        IReadOnlyList<PrescriptionDto> items = await ToDtosAsync(prescriptions, cancellationToken);
        return Result<IReadOnlyList<PrescriptionDto>>.Ok(items);
    }

    public async Task<Result> DeleteAsync(Guid doctorUserId, Guid prescriptionId, CancellationToken cancellationToken = default)
    {
        var doctor = await GetApprovedDoctorAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return Result.Fail(DoctorOnlyMessage);
        }

        var prescription = await _prescriptions.GetByIdAsync(prescriptionId, true, cancellationToken);
        if (prescription is null || prescription.IsDeleted)
        {
            return Result.Fail(NotFoundMessage);
        }

        if (prescription.DoctorProfileId != doctor.Id)
        {
            return Result.Fail("You can only delete prescriptions that you wrote.");
        }

        prescription.Delete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<IReadOnlyList<PrescriptionDto>>> GetMyPrescriptionsAsync(Guid patientUserId, CancellationToken cancellationToken = default)
    {
        if (patientUserId == Guid.Empty)
        {
            return Result<IReadOnlyList<PrescriptionDto>>.Fail("A signed-in patient is required.");
        }

        var patient = await _patients.GetByUserIdAsync(patientUserId, false, cancellationToken);
        if (patient is null)
        {
            return Result<IReadOnlyList<PrescriptionDto>>.Fail("Patient profile was not found.");
        }

        var prescriptions = await _prescriptions.GetByPatientAsync(patient.Id, cancellationToken);
        IReadOnlyList<PrescriptionDto> items = await ToDtosAsync(prescriptions, cancellationToken);
        return Result<IReadOnlyList<PrescriptionDto>>.Ok(items);
    }

    public async Task<Result<PrescriptionDto>> GetMyPrescriptionAsync(Guid patientUserId, Guid prescriptionId, CancellationToken cancellationToken = default)
    {
        if (patientUserId == Guid.Empty)
        {
            return Result<PrescriptionDto>.Fail("A signed-in patient is required.");
        }

        var patient = await _patients.GetByUserIdAsync(patientUserId, false, cancellationToken);
        if (patient is null)
        {
            return Result<PrescriptionDto>.Fail("Patient profile was not found.");
        }

        var prescription = await _prescriptions.GetByIdAsync(prescriptionId, false, cancellationToken);

        // Same message for "missing" and "belongs to someone else".
        if (prescription is null || prescription.IsDeleted || prescription.PatientProfileId != patient.Id)
        {
            return Result<PrescriptionDto>.Fail(NotFoundMessage);
        }

        var doctorName = await GetDoctorNameAsync(prescription.DoctorProfileId, cancellationToken);
        return Result<PrescriptionDto>.Ok(prescription.ToDto(doctorName));
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

    private async Task<List<PrescriptionDto>> ToDtosAsync(IReadOnlyList<Prescription> prescriptions, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var doctorId in prescriptions.Select(p => p.DoctorProfileId).Distinct())
        {
            names[doctorId] = await GetDoctorNameAsync(doctorId, cancellationToken);
        }

        return prescriptions.Select(p => p.ToDto(names[p.DoctorProfileId])).ToList();
    }
}
