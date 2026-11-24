using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Tests;

public sealed class AccountProvisioningServiceTests
{
    private static readonly Guid SpecializationId = Guid.NewGuid();

    [Theory]
    [InlineData("010 1234 5678", "+201012345678")]
    [InlineData("(010) 1234-5678", "+201012345678")]
    [InlineData("+20 1012345678", "+201012345678")]
    [InlineData("0020-1012345678", "+201012345678")]
    [InlineData("01112345678", "+201112345678")]
    [InlineData("01212345678", "+201212345678")]
    [InlineData("01512345678", "+201512345678")]
    public void Egyptian_mobile_normalization_accepts_supported_local_and_international_forms(string input, string expected)
    {
        Assert.True(EgyptianMobileNumber.TryNormalize(input, out var canonical, out var error));
        Assert.Equal(expected, canonical);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0201012345678")]
    [InlineData("201012345678")]
    [InlineData("0111234567")]
    [InlineData("010123456789")]
    [InlineData("+201912345678")]
    [InlineData("+441012345678")]
    [InlineData("+20 10 12x4 5678")]
    public void Egyptian_mobile_normalization_rejects_invalid_or_unsupported_numbers(string? input)
    {
        Assert.False(EgyptianMobileNumber.TryNormalize(input, out var canonical, out var error));
        Assert.Empty(canonical);
        Assert.Equal("Enter a valid Egyptian mobile number.", error);
    }

    [Fact]
    public async Task Doctor_creation_is_atomic_and_rolls_back_identity_user_when_profile_save_fails()
    {
        var fixture = new Fixture { FailOnSave = true };
        var service = fixture.CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDoctorAsync(
            new CreateDoctorAccountRequest("doctor@example.invalid", "Dr Example", SpecializationId, 25), CancellationToken.None));

        Assert.Empty(fixture.Identity.Users);
        Assert.Empty(fixture.Repository.Doctors);
        Assert.False(fixture.UnitOfWork.WasCommitted);
        Assert.True(fixture.UnitOfWork.WasRolledBack);
    }

    [Fact]
    public async Task Doctor_creation_requires_a_positive_duration_and_has_no_default()
    {
        var fixture = new Fixture();
        var result = await fixture.CreateService().CreateDoctorAsync(
            new CreateDoctorAccountRequest("doctor@example.invalid", "Dr Example", SpecializationId, 0), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Identity.Users);
        Assert.Empty(fixture.Repository.Doctors);
    }

    [Fact]
    public async Task Staff_creation_rejects_an_existing_email_without_creating_an_account()
    {
        var fixture = new Fixture();
        fixture.Identity.ExistingEmail = "doctor@example.invalid";
        var result = await fixture.CreateService().CreateDoctorAsync(
            new CreateDoctorAccountRequest("doctor@example.invalid", "Dr Example", SpecializationId, 25), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Identity.Users);
        Assert.Contains("already exists", Assert.Single(result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Successful_account_creation_returns_a_random_temporary_password_and_requires_change()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var first = await service.CreateReceptionistAsync(
            new CreateStaffAccountRequest("reception@example.invalid", "Reception Example"), CancellationToken.None);
        var second = await service.CreateReceptionistAsync(
            new CreateStaffAccountRequest("reception2@example.invalid", "Reception Example 2"), CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotNull(first.TemporaryPassword);
        Assert.NotNull(second.TemporaryPassword);
        Assert.NotEqual(first.TemporaryPassword, second.TemporaryPassword);
        Assert.True(fixture.Identity.LastMustChangePassword);
        Assert.True(fixture.UnitOfWork.WasCommitted);
    }

    [Fact]
    public async Task Patient_creation_uses_canonical_phone_for_username_and_profile()
    {
        var fixture = new Fixture();
        var result = await fixture.CreateService().CreatePatientAsync(
            new CreatePatientAccountRequest("010 1234 5678", "Patient Example"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("+201012345678", result.CanonicalPhone);
        Assert.Equal("+201012345678", fixture.Identity.LastUserName);
        Assert.Equal("+201012345678", Assert.Single(fixture.Repository.Patients).PhoneNumber);
        Assert.True(fixture.Identity.LastMustChangePassword);
    }

    [Fact]
    public async Task Patient_self_registration_uses_canonical_phone_optional_email_and_patient_chosen_password_without_change_gate()
    {
        var fixture = new Fixture();
        var result = await fixture.CreateService().RegisterPatientAsync(
            new RegisterPatientAccountRequest("011 1234 5678", "Self Register", " person@example.invalid ", "ChosenPassword123!"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.TemporaryPassword);
        Assert.Equal("+201112345678", result.CanonicalPhone);
        Assert.Equal("+201112345678", fixture.Identity.LastUserName);
        Assert.Equal("person@example.invalid", fixture.Identity.LastEmail);
        Assert.Equal("ChosenPassword123!", fixture.Identity.LastPassword);
        Assert.Equal("Patient", fixture.Identity.LastRole);
        Assert.False(fixture.Identity.LastMustChangePassword);
        Assert.Equal("+201112345678", Assert.Single(fixture.Repository.Patients).PhoneNumber);
        Assert.True(fixture.UnitOfWork.WasCommitted);
    }

    [Fact]
    public async Task Patient_self_registration_allows_missing_email()
    {
        var fixture = new Fixture();
        var result = await fixture.CreateService().RegisterPatientAsync(
            new RegisterPatientAccountRequest("01512345678", "Self Register", " ", "ChosenPassword123!"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(fixture.Identity.LastEmail);
    }

    [Fact]
    public async Task Patient_self_registration_rejects_an_existing_canonical_phone()
    {
        var fixture = new Fixture();
        fixture.Repository.ExistingPatientPhone = "+201012345678";

        var result = await fixture.CreateService().RegisterPatientAsync(
            new RegisterPatientAccountRequest("01012345678", "Duplicate", null, "ChosenPassword123!"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Identity.Users);
        Assert.Empty(fixture.Repository.Patients);
        Assert.Contains("phone", Assert.Single(result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patient_self_registration_propagates_identity_username_conflict_without_creating_profile()
    {
        var fixture = new Fixture();
        fixture.Identity.CreateFailure = "User name is already taken.";

        var result = await fixture.CreateService().RegisterPatientAsync(
            new RegisterPatientAccountRequest("01012345678", "Duplicate", null, "ChosenPassword123!"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Repository.Patients);
        Assert.Contains("User name", Assert.Single(result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture
    {
        public FakeIdentityStore Identity { get; } = new();
        public FakeAccountRepository Repository { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; }
        public bool FailOnSave { get; init; }

        public Fixture() => UnitOfWork = new FakeUnitOfWork(this);

        public IAccountProvisioningService CreateService()
        {
            Repository.FailOnSave = FailOnSave;
            return new AccountProvisioningService(Identity, Repository, UnitOfWork);
        }

        public void RollbackSnapshot()
        {
            Identity.Users.Clear();
            Repository.Doctors.Clear();
            Repository.Patients.Clear();
        }
    }

    private sealed class FakeIdentityStore : IIdentityAccountStore
    {
        public List<Guid> Users { get; } = [];
        public string? LastUserName { get; private set; }
        public bool LastMustChangePassword { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastPassword { get; private set; }
        public string? LastRole { get; private set; }
        public string? ExistingEmail { get; set; }
        public string? CreateFailure { get; set; }

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(ExistingEmail, email, StringComparison.OrdinalIgnoreCase));

        public Task<IdentityAccountCreateResult> CreateAsync(string userName, string? email, string fullName, string? phoneNumber, string role, string password, bool mustChangePassword, CancellationToken cancellationToken)
        {
            if (CreateFailure is not null)
                return Task.FromResult(IdentityAccountCreateResult.Failure([CreateFailure]));
            Users.Add(Guid.NewGuid());
            LastUserName = userName;
            LastEmail = email;
            LastPassword = password;
            LastRole = role;
            LastMustChangePassword = mustChangePassword;
            return Task.FromResult(IdentityAccountCreateResult.Success(Users[^1]));
        }

        public Task<IdentityAccountSnapshot?> FindAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityPasswordResetResult> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAccountRepository : IAccountProvisioningRepository
    {
        public List<Doctor> Doctors { get; } = [];
        public List<Patient> Patients { get; } = [];
        public bool FailOnSave { get; set; }
        public string? ExistingPatientPhone { get; set; }
        public Task<bool> PatientPhoneExistsAsync(string canonicalPhone, CancellationToken cancellationToken) => Task.FromResult(ExistingPatientPhone == canonicalPhone);
        public Task<bool> SpecializationExistsAsync(Guid specializationId, CancellationToken cancellationToken) => Task.FromResult(specializationId == SpecializationId);
        public Task<IReadOnlyList<SpecializationChoice>> GetSpecializationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SpecializationChoice>>([]);
        public Task AddPatientAsync(Patient patient, CancellationToken cancellationToken) { Patients.Add(patient); return Task.CompletedTask; }
        public Task AddDoctorAsync(Doctor doctor, CancellationToken cancellationToken) { Doctors.Add(doctor); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => FailOnSave
            ? throw new InvalidOperationException("Simulated profile persistence failure.")
            : Task.CompletedTask;

    }

    private sealed class FakeUnitOfWork(Fixture fixture) : IUnitOfWork
    {
        public bool WasCommitted { get; private set; }
        public bool WasRolledBack { get; private set; }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            fixture.RollbackSnapshot();
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(this, fixture));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => fixture.FailOnSave
            ? throw new InvalidOperationException("Simulated profile persistence failure.")
            : Task.CompletedTask;

        public Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) => throw new NotSupportedException();

        private sealed class FakeTransaction(FakeUnitOfWork owner, Fixture fixture) : IUnitOfWorkTransaction
        {
            private bool _committed;
            public Task CommitAsync(CancellationToken cancellationToken) { _committed = true; owner.WasCommitted = true; return Task.CompletedTask; }
            public ValueTask DisposeAsync()
            {
                if (!_committed)
                {
                    fixture.RollbackSnapshot();
                    owner.WasRolledBack = true;
                }
                return ValueTask.CompletedTask;
            }
        }
    }
}
