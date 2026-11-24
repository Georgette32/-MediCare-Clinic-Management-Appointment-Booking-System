using MediCare.Application.Abstractions;
using MediCare.Application.Services;

namespace MediCare.Tests;

public sealed class PasswordResetServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid SecondAdminId = Guid.NewGuid();
    private static readonly Guid ReceptionId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly Guid PatientUserId = Guid.NewGuid();
    private static readonly Guid PatientProfileId = Guid.NewGuid();
    private static readonly Guid DoctorProfileId = Guid.NewGuid();

    [Fact]
    public async Task Admin_resets_a_doctor_and_gets_a_one_time_temporary_password()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResetStaffPasswordAsync(DoctorId, AdminId, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Doctor", result.Role);
        Assert.Equal("doctor@example.invalid", result.UserName);
        Assert.Equal(DoctorId, result.UserId);
        Assert.False(string.IsNullOrEmpty(result.TemporaryPassword));
        Assert.Equal(result.TemporaryPassword, fixture.Identity.Accounts[DoctorId].Password);
        Assert.True(fixture.Identity.Accounts[DoctorId].MustChangePassword);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Admin_can_reset_a_receptionist()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResetStaffPasswordAsync(ReceptionId, AdminId, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Receptionist", result.Role);
    }

    [Fact]
    public async Task Staff_reset_never_targets_an_admin_account()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResetStaffPasswordAsync(SecondAdminId, AdminId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.TemporaryPassword);
        Assert.Equal(0, fixture.Identity.ResetCalls);
    }

    [Fact]
    public async Task Admin_cannot_reset_their_own_password_through_the_staff_path()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResetStaffPasswordAsync(AdminId, AdminId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, fixture.Identity.ResetCalls);
    }

    [Fact]
    public async Task Staff_reset_refuses_patient_accounts_and_non_admin_actors()
    {
        var fixture = new Fixture();

        Assert.False((await fixture.Service.ResetStaffPasswordAsync(PatientUserId, AdminId, CancellationToken.None)).Succeeded);
        Assert.False((await fixture.Service.ResetStaffPasswordAsync(DoctorId, ReceptionId, CancellationToken.None)).Succeeded);
        Assert.Equal(0, fixture.Identity.ResetCalls);
    }

    [Fact]
    public async Task Staff_reset_refuses_inactive_and_unknown_accounts()
    {
        var fixture = new Fixture();
        fixture.Identity.Accounts[DoctorId].IsActive = false;

        Assert.False((await fixture.Service.ResetStaffPasswordAsync(DoctorId, AdminId, CancellationToken.None)).Succeeded);
        Assert.False((await fixture.Service.ResetStaffPasswordAsync(Guid.NewGuid(), AdminId, CancellationToken.None)).Succeeded);
        Assert.Equal(0, fixture.Identity.ResetCalls);
    }

    [Fact]
    public async Task Reception_resets_a_patient_password_through_the_patient_profile()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResetPatientPasswordAsync(PatientProfileId, ReceptionId, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Patient", result.Role);
        Assert.Equal("+201012345678", result.UserName);
        Assert.True(fixture.Identity.Accounts[PatientUserId].MustChangePassword);
    }

    [Fact]
    public async Task Admin_can_reset_a_patient_password_too()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ResetPatientPasswordAsync(PatientProfileId, AdminId, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task Patient_reset_refuses_staff_accounts_unknown_profiles_and_non_reception_actors()
    {
        var fixture = new Fixture();
        fixture.Lookup.Map[DoctorProfileId] = DoctorId;

        Assert.False((await fixture.Service.ResetPatientPasswordAsync(DoctorProfileId, ReceptionId, CancellationToken.None)).Succeeded);
        Assert.False((await fixture.Service.ResetPatientPasswordAsync(Guid.NewGuid(), ReceptionId, CancellationToken.None)).Succeeded);
        Assert.False((await fixture.Service.ResetPatientPasswordAsync(PatientProfileId, DoctorId, CancellationToken.None)).Succeeded);
        Assert.Equal(0, fixture.Identity.ResetCalls);
    }

    [Fact]
    public async Task A_failed_identity_reset_returns_a_friendly_failure_and_commits_nothing()
    {
        var fixture = new Fixture(failReset: true);
        var result = await fixture.Service.ResetStaffPasswordAsync(DoctorId, AdminId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.TemporaryPassword);
        Assert.Equal("The password could not be reset. Try again.", result.Message);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    private sealed class Fixture
    {
        public Fixture(bool failReset = false)
        {
            Identity = new FakeIdentity { FailReset = failReset };
            Identity.Accounts[AdminId] = Account(AdminId, "admin@example.invalid", "Admin", "Admin");
            Identity.Accounts[SecondAdminId] = Account(SecondAdminId, "admin2@example.invalid", "Second Admin", "Admin");
            Identity.Accounts[ReceptionId] = Account(ReceptionId, "reception@example.invalid", "Reception", "Receptionist");
            Identity.Accounts[DoctorId] = Account(DoctorId, "doctor@example.invalid", "Doctor", "Doctor");
            Identity.Accounts[PatientUserId] = Account(PatientUserId, "+201012345678", "Patient", "Patient");
            Lookup.Map[PatientProfileId] = PatientUserId;
        }

        public FakeIdentity Identity { get; }
        public FakePatientLookup Lookup { get; } = new();
        public FlowUnitOfWork UnitOfWork { get; } = new();
        public IPasswordResetService Service => new PasswordResetService(Identity, Lookup, UnitOfWork);

        private static FakeAccount Account(Guid id, string userName, string fullName, string role) =>
            new() { UserId = id, UserName = userName, FullName = fullName, Roles = [role], IsActive = true };
    }

    private sealed class FakeAccount
    {
        public Guid UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public IReadOnlyList<string> Roles { get; init; } = [];
        public bool IsActive { get; set; }
        public string? Password { get; set; }
        public bool MustChangePassword { get; set; }
    }

    private sealed class FakeIdentity : IIdentityAccountStore
    {
        public Dictionary<Guid, FakeAccount> Accounts { get; } = [];
        public bool FailReset { get; init; }
        public int ResetCalls { get; private set; }

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IdentityAccountCreateResult> CreateAsync(string userName, string? email, string fullName, string? phoneNumber, string role, string password, bool mustChangePassword, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityAccountSnapshot?> FindAccountAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!Accounts.TryGetValue(userId, out var account))
                return Task.FromResult<IdentityAccountSnapshot?>(null);
            return Task.FromResult<IdentityAccountSnapshot?>(new IdentityAccountSnapshot(account.UserId, account.UserName, account.FullName, account.IsActive, account.Roles));
        }

        public Task<IdentityPasswordResetResult> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken)
        {
            ResetCalls++;
            if (FailReset)
                return Task.FromResult(IdentityPasswordResetResult.Failure(["Password rejected."]));
            var account = Accounts[userId];
            account.Password = temporaryPassword;
            account.MustChangePassword = true;
            return Task.FromResult(IdentityPasswordResetResult.Success());
        }
    }

    private sealed class FakePatientLookup : IPatientAccountLookup
    {
        public Dictionary<Guid, Guid> Map { get; } = [];

        public Task<Guid?> FindApplicationUserIdAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult(Map.TryGetValue(patientId, out var userId) ? (Guid?)userId : null);
    }
}
