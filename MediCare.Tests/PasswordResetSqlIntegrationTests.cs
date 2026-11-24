using MediCare.Application.Services;
using MediCare.Infrastructure;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Tests;

/// <summary>
/// Real Identity (production registration) against the disposable LocalDB database. Verifies that a reset account can sign in
/// with the one-time password, is flagged MustChangePassword, has its lockout cleared and a new security stamp, and that the
/// old password no longer works.
/// </summary>
[Collection(SqlServerConstraintCollection.Name)]
public sealed class PasswordResetSqlIntegrationTests(LocalDbDatabaseFixture database)
{
    private const string OriginalPassword = "Patient-Pa55word!";

    [Fact]
    public async Task Reset_patient_can_sign_in_with_temporary_password_and_is_flagged_and_unlocked()
    {
        await using var provider = BuildProvider(database.ConnectionString);
        var phone = UniqueEgyptianPhone();
        var receptionName = $"reception-{Guid.NewGuid():N}@example.invalid";
        string stampBefore;
        Guid patientId;

        await using (var setup = provider.CreateAsyncScope())
        {
            var services = setup.ServiceProvider;
            await EnsureRolesAsync(services, MediCareRoles.Admin, MediCareRoles.Receptionist, MediCareRoles.Patient, MediCareRoles.Doctor);
            var context = services.GetRequiredService<MediCareDbContext>();
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var provisioning = new AccountProvisioningService(new IdentityAccountStore(users), new EfAccountProvisioningRepository(context), new EfUnitOfWork(context));

            var registered = await provisioning.RegisterPatientAsync(new RegisterPatientAccountRequest(phone, "Reset Patient", null, OriginalPassword), CancellationToken.None);
            Assert.True(registered.Succeeded, string.Join(", ", registered.Errors));
            var receptionist = await new IdentityAccountStore(users).CreateAsync(receptionName, receptionName, "Reception", null, MediCareRoles.Receptionist, OriginalPassword, false, CancellationToken.None);
            Assert.True(receptionist.Succeeded, string.Join(", ", receptionist.Errors));

            var patient = await context.Patients.AsNoTracking().SingleAsync(item => item.PhoneNumber == phone);
            patientId = patient.Id;

            var account = await users.FindByNameAsync(phone);
            Assert.NotNull(account);
            Assert.True(account!.LockoutEnabled, "New accounts must be lockout-enabled by the production policy.");
            for (var attempt = 0; attempt < 5; attempt++)
                await users.AccessFailedAsync(account);
            Assert.True(await users.IsLockedOutAsync(account));
            stampBefore = account.SecurityStamp!;
        }

        string temporaryPassword;
        await using (var reset = provider.CreateAsyncScope())
        {
            var services = reset.ServiceProvider;
            var context = services.GetRequiredService<MediCareDbContext>();
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var receptionId = (await users.FindByNameAsync(receptionName))!.Id;
            var service = new PasswordResetService(new IdentityAccountStore(users), new EfPatientAccountLookup(context), new EfUnitOfWork(context));

            var result = await service.ResetPatientPasswordAsync(patientId, receptionId, CancellationToken.None);
            Assert.True(result.Succeeded, result.Message);
            temporaryPassword = result.TemporaryPassword!;
            Assert.False(string.IsNullOrWhiteSpace(temporaryPassword));
        }

        await using (var verify = provider.CreateAsyncScope())
        {
            var users = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(phone);
            Assert.NotNull(user);
            Assert.True(user!.MustChangePassword);
            Assert.False(await users.IsLockedOutAsync(user));
            Assert.Equal(0, user.AccessFailedCount);
            Assert.Null(user.LockoutEnd);
            Assert.NotEqual(stampBefore, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, temporaryPassword));
            Assert.False(await users.CheckPasswordAsync(user, OriginalPassword));
        }
    }

    [Fact]
    public async Task Reset_doctor_by_admin_gets_a_temporary_password_that_passes_identity_and_flags_the_account()
    {
        await using var provider = BuildProvider(database.ConnectionString);
        var doctorName = $"doctor-{Guid.NewGuid():N}@example.invalid";
        var adminName = $"admin-{Guid.NewGuid():N}@example.invalid";

        await using (var setup = provider.CreateAsyncScope())
        {
            var services = setup.ServiceProvider;
            await EnsureRolesAsync(services, MediCareRoles.Admin, MediCareRoles.Receptionist, MediCareRoles.Patient, MediCareRoles.Doctor);
            var store = new IdentityAccountStore(services.GetRequiredService<UserManager<ApplicationUser>>());
            Assert.True((await store.CreateAsync(doctorName, doctorName, "Dr Reset", null, MediCareRoles.Doctor, OriginalPassword, false, CancellationToken.None)).Succeeded);
            Assert.True((await store.CreateAsync(adminName, adminName, "Admin", null, MediCareRoles.Admin, OriginalPassword, false, CancellationToken.None)).Succeeded);
        }

        string temporaryPassword;
        await using (var reset = provider.CreateAsyncScope())
        {
            var services = reset.ServiceProvider;
            var context = services.GetRequiredService<MediCareDbContext>();
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var doctorId = (await users.FindByNameAsync(doctorName))!.Id;
            var adminId = (await users.FindByNameAsync(adminName))!.Id;
            var service = new PasswordResetService(new IdentityAccountStore(users), new EfPatientAccountLookup(context), new EfUnitOfWork(context));

            Assert.False((await service.ResetStaffPasswordAsync(adminId, adminId, CancellationToken.None)).Succeeded);
            var result = await service.ResetStaffPasswordAsync(doctorId, adminId, CancellationToken.None);
            Assert.True(result.Succeeded, result.Message);
            temporaryPassword = result.TemporaryPassword!;
        }

        await using (var verify = provider.CreateAsyncScope())
        {
            var users = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var doctor = await users.FindByNameAsync(doctorName);
            Assert.NotNull(doctor);
            Assert.True(doctor!.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(doctor, temporaryPassword));
            Assert.False(await users.CheckPasswordAsync(doctor, OriginalPassword));
        }
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MediCareDbContext>(options => options.UseSqlServer(connectionString));
        services.AddMediCareIdentity();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureRolesAsync(IServiceProvider services, params string[] roleNames)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in roleNames)
        {
            if (await roles.RoleExistsAsync(role))
                continue;
            var created = await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
            Assert.True(created.Succeeded, $"Could not create role '{role}'.");
        }
    }

    private static string UniqueEgyptianPhone() => "+2010" + Random.Shared.Next(100_000_000).ToString("D8");
}
