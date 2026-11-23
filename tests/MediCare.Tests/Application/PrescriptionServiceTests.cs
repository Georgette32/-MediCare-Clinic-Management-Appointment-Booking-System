using MediCare.Application.DTOs.Prescriptions;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Services;
using MediCare.Application.Validation;
using MediCare.Domain.Entities;
using Moq;

namespace MediCare.Tests.Application;

public class PrescriptionServiceTests
{
    private readonly Mock<IPrescriptionRepository> _prescriptions = new();
    private readonly Mock<IMedicalRecordRepository> _medicalRecords = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private PrescriptionService CreateService() => new(
        _prescriptions.Object,
        _medicalRecords.Object,
        _doctors.Object,
        _patients.Object,
        _identity.Object,
        _unitOfWork.Object,
        new CreatePrescriptionRequestValidator());

    private static DoctorProfile CreateDoctor(bool approved = true)
    {
        var doctor = new DoctorProfile(Guid.NewGuid(), Guid.NewGuid(), "MED-1234", 100m, 5, "Bio");
        if (approved)
        {
            doctor.Approve(Guid.NewGuid());
        }

        return doctor;
    }

    private void SetupDoctor(DoctorProfile doctor)
    {
        _doctors
            .Setup(x => x.GetByUserIdAsync(doctor.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _doctors
            .Setup(x => x.GetByIdAsync(doctor.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _identity
            .Setup(x => x.FindByIdAsync(doctor.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUserInfo(
                doctor.UserId, "doctor@example.com", "Dr. Alice Smith", null, true, true, DateTime.UtcNow));
    }

    private void SetupAccess(DoctorProfile doctor, Guid patientId, bool allowed)
    {
        _medicalRecords
            .Setup(x => x.DoctorHasCompletedAppointmentWithPatientAsync(
                doctor.Id, patientId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);
    }

    private static PrescriptionItemRequest Item(string name = "Amoxicillin") => new()
    {
        MedicationName = name,
        Dosage = "500 mg",
        Frequency = "3 times daily",
        DurationInDays = 7
    };

    private static CreatePrescriptionRequest ValidRequest(Guid patientId, params PrescriptionItemRequest[] items) => new()
    {
        PatientProfileId = patientId,
        Items = items.Length == 0 ? new List<PrescriptionItemRequest> { Item() } : items.ToList()
    };

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task CreateAsync_RequiresAtLeastOneMedication()
    {
        var request = new CreatePrescriptionRequest { PatientProfileId = Guid.NewGuid() };

        var result = await CreateService().CreateAsync(Guid.NewGuid(), request);

        Assert.False(result.Succeeded);
        _doctors.Verify(x => x.GetByUserIdAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_RejectsAnInvalidDuration()
    {
        var item = Item();
        item.DurationInDays = 0;

        var result = await CreateService().CreateAsync(Guid.NewGuid(), ValidRequest(Guid.NewGuid(), item));

        Assert.False(result.Succeeded);
        Assert.Contains("duration", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RejectsDoctorWhoIsNotApproved()
    {
        var doctor = CreateDoctor(approved: false);
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, true);

        var result = await CreateService().CreateAsync(doctor.UserId, ValidRequest(patientId));

        Assert.False(result.Succeeded);
        _prescriptions.Verify(x => x.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_RequiresACompletedAppointmentWithThePatient()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, false);

        var result = await CreateService().CreateAsync(doctor.UserId, ValidRequest(patientId));

        Assert.False(result.Succeeded);
        Assert.Contains("completed appointment", result.Error, StringComparison.OrdinalIgnoreCase);
        _prescriptions.Verify(x => x.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_RejectsAMedicalRecordOfAnotherPatient()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, true);

        var otherPatientsRecord = new MedicalRecord(Guid.NewGuid(), doctor.Id, null, "Cough", "Bronchitis", null, null);
        _medicalRecords
            .Setup(x => x.GetByIdAsync(otherPatientsRecord.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherPatientsRecord);

        var request = ValidRequest(patientId);
        request.MedicalRecordId = otherPatientsRecord.Id;

        var result = await CreateService().CreateAsync(doctor.UserId, request);

        Assert.False(result.Succeeded);
        Assert.Contains("does not belong", result.Error, StringComparison.OrdinalIgnoreCase);
        _prescriptions.Verify(x => x.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SavesThePrescriptionWithAllItems()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, true);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().CreateAsync(
            doctor.UserId,
            ValidRequest(patientId, Item("Amoxicillin"), Item("Ibuprofen")));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("Dr. Alice Smith", result.Value.DoctorName);
        _prescriptions.Verify(x => x.AddAsync(
            It.Is<Prescription>(p => p.PatientProfileId == patientId && p.Items.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public async Task DeleteAsync_OnlyTheAuthorCanDelete()
    {
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var prescription = new Prescription(Guid.NewGuid(), Guid.NewGuid(), null, null);
        _prescriptions
            .Setup(x => x.GetByIdAsync(prescription.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescription);

        var result = await CreateService().DeleteAsync(doctor.UserId, prescription.Id);

        Assert.False(result.Succeeded);
        Assert.False(prescription.IsDeleted);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesTheAuthorsPrescription()
    {
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var prescription = new Prescription(Guid.NewGuid(), doctor.Id, null, null);
        _prescriptions
            .Setup(x => x.GetByIdAsync(prescription.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescription);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().DeleteAsync(doctor.UserId, prescription.Id);

        Assert.True(result.Succeeded);
        Assert.True(prescription.IsDeleted);
    }

    // ------------------------------------------------------------ doctor access

    [Fact]
    public async Task GetPatientPrescriptionsForDoctorAsync_DeniesADoctorWithoutAccess()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, false);

        var result = await CreateService().GetPatientPrescriptionsForDoctorAsync(doctor.UserId, patientId);

        Assert.False(result.Succeeded);
        _prescriptions.Verify(x => x.GetByPatientAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------- patient side

    [Fact]
    public async Task GetMyPrescriptionAsync_HidesAPrescriptionOfAnotherPatient()
    {
        var patient = new PatientProfile(Guid.NewGuid());
        _patients
            .Setup(x => x.GetByUserIdAsync(patient.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        var prescription = new Prescription(Guid.NewGuid(), Guid.NewGuid(), null, null);
        _prescriptions
            .Setup(x => x.GetByIdAsync(prescription.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescription);

        var result = await CreateService().GetMyPrescriptionAsync(patient.UserId, prescription.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMyPrescriptionAsync_ReturnsThePatientsOwnPrescription()
    {
        var patient = new PatientProfile(Guid.NewGuid());
        _patients
            .Setup(x => x.GetByUserIdAsync(patient.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        var doctor = CreateDoctor();
        SetupDoctor(doctor);

        var prescription = new Prescription(patient.Id, doctor.Id, null, null);
        prescription.AddItem("Amoxicillin", "500 mg", "daily", 5, null);
        _prescriptions
            .Setup(x => x.GetByIdAsync(prescription.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescription);

        var result = await CreateService().GetMyPrescriptionAsync(patient.UserId, prescription.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!.Items);
    }
}
