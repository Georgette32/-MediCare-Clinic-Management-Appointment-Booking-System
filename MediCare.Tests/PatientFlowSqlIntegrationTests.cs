using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Tests;

/// <summary>
/// Runs the new EF read queries and the Identity email write against the disposable LocalDB database, so the LINQ
/// translation and the filtered unique EmailIndex are exercised for real.
/// </summary>
[Collection(SqlServerConstraintCollection.Name)]
public sealed class PatientFlowSqlIntegrationTests
{
    private const string Password = "Patient-Pa55word!";
    private readonly LocalDbDatabaseFixture _database;

    public PatientFlowSqlIntegrationTests(LocalDbDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Query_repository_returns_patient_rows_replacement_links_and_phone_search()
    {
        var now = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        var day = new DateOnly(2026, 12, 15);
        var phone = "+2011" + Random.Shared.Next(10_000_000, 99_999_999);
        var email = $"sara-{Guid.NewGuid():N}@example.invalid";

        await using var seed = _database.CreateContext();
        var specialization = new Specialization(Guid.NewGuid(), "Cardio-" + Guid.NewGuid().ToString("N"));
        var doctorUser = await CreateUserAsync(seed, "doc-" + Guid.NewGuid().ToString("N"), email: null, "Dr. Query");
        var doctor = new Doctor(Guid.NewGuid(), doctorUser.Id, "Dr. Query", specialization.Id, 20);
        var patientUser = await CreateUserAsync(seed, phone, email, "Sara Query");
        var patient = new Patient(Guid.NewGuid(), patientUser.Id, "Sara Query", phone);
        seed.Specializations.Add(specialization);
        seed.Doctors.Add(doctor);
        seed.Patients.Add(patient);
        await seed.SaveChangesAsync();

        var original = Appointment.Request(patient.Id, doctor.Id, day, new TimeOnly(9, 0), new TimeOnly(9, 20), now);
        original.Confirm(now);
        seed.Appointments.Add(original);
        await seed.SaveChangesAsync();

        var replacement = Appointment.Request(patient.Id, doctor.Id, day, new TimeOnly(10, 0), new TimeOnly(10, 20), now, original.Id);
        seed.Appointments.Add(replacement);
        await seed.SaveChangesAsync();

        await using var read = _database.CreateContext();
        var queries = new EfAppointmentQueryRepository(read);

        Assert.Equal(patient.Id, await queries.GetPatientIdForUserAsync(patientUser.Id, CancellationToken.None));

        var rows = await queries.GetForPatientAsync(patient.Id, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        var replacementRow = rows.Single(row => row.Id == replacement.Id);
        Assert.Equal(specialization.Name, replacementRow.Specialization);
        Assert.Equal("Dr. Query", replacementRow.DoctorName);
        Assert.Equal(original.AppointmentDate, replacementRow.OriginalDate);
        Assert.Equal(original.StartTime, replacementRow.OriginalStartTime);

        var live = await queries.GetLiveReplacementsAsync([original.Id], CancellationToken.None);
        var link = Assert.Single(live);
        Assert.Equal(replacement.Id, link.ReplacementId);
        Assert.Equal(AppointmentStatus.Pending, link.Status);

        var byPhone = await queries.SearchPatientsAsync(phone, null, null, CancellationToken.None);
        var found = Assert.Single(byPhone);
        Assert.Equal(email, found.Email);
        Assert.Equal(patient.Id, found.PatientId);

        var byDigits = await queries.SearchPatientsAsync(null, phone[^6..], null, CancellationToken.None);
        Assert.Contains(byDigits, item => item.PatientId == patient.Id);

        var byName = await queries.SearchPatientsAsync(null, null, "Sara Query", CancellationToken.None);
        Assert.Contains(byName, item => item.PatientId == patient.Id);

        var summary = await queries.GetPatientAsync(patient.Id, CancellationToken.None);
        Assert.Equal(email, summary!.Email);
        Assert.Equal(patientUser.Id, summary.ApplicationUserId);

        var dayRows = await queries.GetForDateAsync(day, doctor.Id, CancellationToken.None);
        Assert.Equal(2, dayRows.Count);
        Assert.NotNull(await queries.GetRecordAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Identity_email_write_maps_database_unique_violation_to_a_failure_and_allows_removal()
    {
        await using var provider = BuildIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var store = new IdentityProfileStore(users);

        var takenEmail = $"taken-{Guid.NewGuid():N}@example.invalid";
        var first = new ApplicationUser { Id = Guid.NewGuid(), UserName = "first-" + Guid.NewGuid().ToString("N"), Email = takenEmail, FullName = "First" };
        var firstCreated = await users.CreateAsync(first, Password);
        Assert.True(firstCreated.Succeeded, string.Join("; ", firstCreated.Errors.Select(error => error.Description)));
        var second = new ApplicationUser { Id = Guid.NewGuid(), UserName = "second-" + Guid.NewGuid().ToString("N"), FullName = "Second" };
        var secondCreated = await users.CreateAsync(second, Password);
        Assert.True(secondCreated.Succeeded, string.Join("; ", secondCreated.Errors.Select(error => error.Description)));

        var duplicate = await store.SetEmailAsync(second.Id, takenEmail, CancellationToken.None);
        Assert.False(duplicate.Succeeded);
        Assert.Equal("An account with this email already exists.", Assert.Single(duplicate.Errors));

        // A failed save leaves the tracked user with a stale concurrency stamp, so later work uses a fresh scope, as each request does.
        await using var nextScope = provider.CreateAsyncScope();
        var nextUsers = nextScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var nextStore = new IdentityProfileStore(nextUsers);

        var added = await nextStore.SetEmailAsync(second.Id, $"second-{Guid.NewGuid():N}@example.invalid", CancellationToken.None);
        Assert.True(added.Succeeded, string.Join("; ", added.Errors));

        var removed = await nextStore.SetEmailAsync(second.Id, null, CancellationToken.None);
        Assert.True(removed.Succeeded, string.Join("; ", removed.Errors));
        var reloaded = await nextUsers.FindByIdAsync(second.Id.ToString());
        Assert.Null(reloaded!.Email);
        Assert.Null(reloaded.NormalizedEmail);
    }

    private static async Task<ApplicationUser> CreateUserAsync(MediCareDbContext context, string userName, string? email, string fullName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email?.ToUpperInvariant(),
            FullName = fullName,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private ServiceProvider BuildIdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MediCareDbContext>(options => options.UseSqlServer(_database.ConnectionString));
        services.AddMediCareIdentity();
        return services.BuildServiceProvider();
    }
}
