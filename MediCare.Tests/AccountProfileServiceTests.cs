using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Tests;

public sealed class AccountProfileServiceTests
{
    private const string PatientPhone = "+201012345678";

    [Fact]
    public async Task Patient_name_change_updates_identity_and_patient_profile_in_one_transaction()
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null);

        var result = await fixture.Service.ChangeFullNameAsync(fixture.UserId, "  Sara Hany  ", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Sara Hany", fixture.Identity.Users[fixture.UserId].FullName);
        Assert.Equal("Sara Hany", fixture.Profiles.Patients.Single().FullName);
        Assert.Equal(1, fixture.Uow.CommitCount);
    }

    [Fact]
    public async Task Doctor_name_change_updates_identity_and_doctor_profile()
    {
        var fixture = Create("Doctor", userName: "hala@clinic.test", email: "hala@clinic.test");

        var result = await fixture.Service.ChangeFullNameAsync(fixture.UserId, "Dr. Hala Adel", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Dr. Hala Adel", fixture.Identity.Users[fixture.UserId].FullName);
        Assert.Equal("Dr. Hala Adel", fixture.Profiles.Doctors.Single().FullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_name_is_refused_without_writes(string name)
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null);

        var result = await fixture.Service.ChangeFullNameAsync(fixture.UserId, name, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, fixture.Identity.UpdateCount);
        Assert.Equal("Sara Old", fixture.Profiles.Patients.Single().FullName);
    }

    [Fact]
    public async Task Name_longer_than_200_characters_is_refused()
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null);

        var result = await fixture.Service.ChangeFullNameAsync(fixture.UserId, new string('a', 201), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, fixture.Identity.UpdateCount);
    }

    [Fact]
    public async Task Patient_can_add_change_and_remove_email()
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null);

        var added = await fixture.Service.ChangeEmailAsync(fixture.UserId, "sara@example.test", CancellationToken.None);
        Assert.True(added.Succeeded);
        Assert.Equal("sara@example.test", fixture.Identity.Users[fixture.UserId].Email);

        var changed = await fixture.Service.ChangeEmailAsync(fixture.UserId, "sara.h@example.test", CancellationToken.None);
        Assert.True(changed.Succeeded);
        Assert.Equal("sara.h@example.test", fixture.Identity.Users[fixture.UserId].Email);

        var removed = await fixture.Service.ChangeEmailAsync(fixture.UserId, "   ", CancellationToken.None);
        Assert.True(removed.Succeeded);
        Assert.Null(fixture.Identity.Users[fixture.UserId].Email);
    }

    [Fact]
    public async Task Email_held_by_another_account_is_refused_and_not_written()
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null, takenEmails: ["taken@example.test"]);

        var result = await fixture.Service.ChangeEmailAsync(fixture.UserId, "TAKEN@example.test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Message);
        Assert.Null(fixture.Identity.Users[fixture.UserId].Email);
    }

    [Fact]
    public async Task Keeping_the_same_email_is_not_reported_as_taken()
    {
        var fixture = Create("Patient", userName: PatientPhone, email: "sara@example.test", takenEmails: ["sara@example.test"]);

        var result = await fixture.Service.ChangeEmailAsync(fixture.UserId, "sara@example.test", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, fixture.Identity.UpdateCount);
    }

    [Fact]
    public async Task Database_unique_violation_on_email_becomes_a_friendly_message()
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null);
        fixture.Identity.UniqueEmailViolationFor = "race@example.test";

        var result = await fixture.Service.ChangeEmailAsync(fixture.UserId, "race@example.test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("An account with this email already exists.", result.Message);
        Assert.Null(fixture.Identity.Users[fixture.UserId].Email);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("sara@")]
    public async Task Malformed_email_is_refused(string email)
    {
        var fixture = Create("Patient", userName: PatientPhone, email: null);

        var result = await fixture.Service.ChangeEmailAsync(fixture.UserId, email, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, fixture.Identity.UpdateCount);
    }

    [Fact]
    public async Task Staff_cannot_change_their_email_here()
    {
        var fixture = Create("Receptionist", userName: "front@clinic.test", email: "front@clinic.test");

        var result = await fixture.Service.ChangeEmailAsync(fixture.UserId, "other@clinic.test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("front@clinic.test", fixture.Identity.Users[fixture.UserId].Email);
    }

    private static Fixture Create(string role, string userName, string? email, string[]? takenEmails = null)
    {
        var userId = Guid.NewGuid();
        var identity = new FlowIdentityProfileStore();
        identity.Users[userId] = new IdentityProfileSnapshot(userId, userName, email, "Sara Old", true, [role]);

        var profiles = new FlowProfileRepository();
        if (role == "Patient")
            profiles.Patients.Add(new Patient(Guid.NewGuid(), userId, "Sara Old", userName));
        if (role == "Doctor")
            profiles.Doctors.Add(new Doctor(Guid.NewGuid(), userId, "Dr. Old", Guid.NewGuid(), 20));

        var uow = new FlowUnitOfWork();
        var service = new AccountProfileService(identity, profiles, new FlowIdentityAccounts(takenEmails ?? []), uow);
        return new Fixture(service, identity, profiles, uow, userId);
    }

    private sealed record Fixture(
        AccountProfileService Service,
        FlowIdentityProfileStore Identity,
        FlowProfileRepository Profiles,
        FlowUnitOfWork Uow,
        Guid UserId);
}
