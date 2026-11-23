using MediCare.Application.DTOs.MedicalRecords;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Services;
using MediCare.Application.Validation;
using MediCare.Domain.Entities;
using Moq;

namespace MediCare.Tests.Application;

public class MedicalRecordServiceTests
{
    private readonly Mock<IMedicalRecordRepository> _records = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private MedicalRecordService CreateService() => new(
        _records.Object,
        _doctors.Object,
        _patients.Object,
        _identity.Object,
        _unitOfWork.Object,
        new CreateMedicalRecordRequestValidator(),
        new UpdateMedicalRecordRequestValidator());

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
        _records
            .Setup(x => x.DoctorHasCompletedAppointmentWithPatientAsync(
                doctor.Id, patientId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);
    }

    private PatientProfile SetupPatient()
    {
        var patient = new PatientProfile(Guid.NewGuid());
        _patients
            .Setup(x => x.GetByUserIdAsync(patient.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);
        return patient;
    }

    private static CreateMedicalRecordRequest ValidRequest(Guid patientId) => new()
    {
        PatientProfileId = patientId,
        Symptoms = "Cough",
        Diagnosis = "Bronchitis",
        TreatmentPlan = "Rest"
    };

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task CreateAsync_RejectsInvalidRequestBeforeLoadingTheDoctor()
    {
        var result = await CreateService().CreateAsync(Guid.NewGuid(), new CreateMedicalRecordRequest());

        Assert.False(result.Succeeded);
        _doctors.Verify(x => x.GetByUserIdAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
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
        Assert.Contains("approved doctors", result.Error, StringComparison.OrdinalIgnoreCase);
        _records.Verify(x => x.AddAsync(It.IsAny<MedicalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _records.Verify(x => x.AddAsync(It.IsAny<MedicalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SavesTheRecordForAnApprovedDoctor()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, true);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().CreateAsync(doctor.UserId, ValidRequest(patientId));

        Assert.True(result.Succeeded);
        Assert.Equal("Bronchitis", result.Value!.Diagnosis);
        Assert.Equal("Dr. Alice Smith", result.Value.DoctorName);
        _records.Verify(x => x.AddAsync(
            It.Is<MedicalRecord>(r => r.PatientProfileId == patientId && r.DoctorProfileId == doctor.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------ update/delete

    [Fact]
    public async Task UpdateAsync_RejectsARecordWrittenByAnotherDoctor()
    {
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var record = new MedicalRecord(Guid.NewGuid(), Guid.NewGuid(), null, "Cough", "Bronchitis", null, null);
        _records
            .Setup(x => x.GetByIdAsync(record.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await CreateService().UpdateAsync(doctor.UserId, new UpdateMedicalRecordRequest
        {
            RecordId = record.Id,
            Symptoms = "Fever",
            Diagnosis = "Flu"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Bronchitis", record.Diagnosis);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesTheDoctorsOwnRecord()
    {
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var record = new MedicalRecord(Guid.NewGuid(), doctor.Id, null, "Cough", "Bronchitis", null, null);
        _records
            .Setup(x => x.GetByIdAsync(record.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().UpdateAsync(doctor.UserId, new UpdateMedicalRecordRequest
        {
            RecordId = record.Id,
            Symptoms = "Fever",
            Diagnosis = "Flu"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Flu", record.Diagnosis);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesTheDoctorsOwnRecord()
    {
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var record = new MedicalRecord(Guid.NewGuid(), doctor.Id, null, "Cough", "Bronchitis", null, null);
        _records
            .Setup(x => x.GetByIdAsync(record.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService().DeleteAsync(doctor.UserId, record.Id);

        Assert.True(result.Succeeded);
        Assert.True(record.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_RejectsARecordWrittenByAnotherDoctor()
    {
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var record = new MedicalRecord(Guid.NewGuid(), Guid.NewGuid(), null, "Cough", "Bronchitis", null, null);
        _records
            .Setup(x => x.GetByIdAsync(record.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await CreateService().DeleteAsync(doctor.UserId, record.Id);

        Assert.False(result.Succeeded);
        Assert.False(record.IsDeleted);
    }

    // ------------------------------------------------------------ doctor access

    [Fact]
    public async Task GetPatientRecordsForDoctorAsync_DeniesADoctorWithoutACompletedAppointment()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, false);

        var result = await CreateService().GetPatientRecordsForDoctorAsync(doctor.UserId, patientId);

        Assert.False(result.Succeeded);
        _records.Verify(x => x.GetByPatientAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPatientRecordsForDoctorAsync_ReturnsRecordsWhenTheDoctorHasAccess()
    {
        var doctor = CreateDoctor();
        var patientId = Guid.NewGuid();
        SetupDoctor(doctor);
        SetupAccess(doctor, patientId, true);
        _records
            .Setup(x => x.GetByPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MedicalRecord>
            {
                new(patientId, doctor.Id, null, "Cough", "Bronchitis", null, null)
            });

        var result = await CreateService().GetPatientRecordsForDoctorAsync(doctor.UserId, patientId);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!);
    }

    // ------------------------------------------------------------- patient side

    [Fact]
    public async Task GetMyRecordAsync_ReturnsThePatientsOwnRecord()
    {
        var patient = SetupPatient();
        var doctor = CreateDoctor();
        SetupDoctor(doctor);
        var record = new MedicalRecord(patient.Id, doctor.Id, null, "Cough", "Bronchitis", null, null);
        _records
            .Setup(x => x.GetByIdAsync(record.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await CreateService().GetMyRecordAsync(patient.UserId, record.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Bronchitis", result.Value!.Diagnosis);
        Assert.Equal("Dr. Alice Smith", result.Value.DoctorName);
    }

    [Fact]
    public async Task GetMyRecordAsync_HidesARecordThatBelongsToAnotherPatient()
    {
        var patient = SetupPatient();
        var record = new MedicalRecord(Guid.NewGuid(), Guid.NewGuid(), null, "Cough", "Bronchitis", null, null);
        _records
            .Setup(x => x.GetByIdAsync(record.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await CreateService().GetMyRecordAsync(patient.UserId, record.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMyRecordAsync_HidesADeletedRecord()
    {
        var patient = SetupPatient();
        var record = new MedicalRecord(patient.Id, Guid.NewGuid(), null, "Cough", "Bronchitis", null, null);
        record.Delete();
        _records
            .Setup(x => x.GetByIdAsync(record.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await CreateService().GetMyRecordAsync(patient.UserId, record.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetMyRecordsAsync_RequiresAPatientProfile()
    {
        _patients
            .Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatientProfile?)null);

        var result = await CreateService().GetMyRecordsAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
    }
}
