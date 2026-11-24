using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace MediCare.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerConstraintCollection : ICollectionFixture<LocalDbDatabaseFixture>
{
    public const string Name = "MediCare SQL Server constraints";
}

[Collection(SqlServerConstraintCollection.Name)]
public sealed class SqlServerConstraintIntegrationTests
{
    private readonly LocalDbDatabaseFixture _database;
    private readonly ITestOutputHelper _output;

    public SqlServerConstraintIntegrationTests(LocalDbDatabaseFixture database, ITestOutputHelper output)
    {
        _database = database;
        _output = output;
    }

    [Fact]
    public async Task Localdb_migration_and_server_version_are_available()
    {
        await using var context = CreateContext();
        var version = await context.Database.SqlQueryRaw<string>("SELECT CONVERT(nvarchar(max), @@VERSION) AS [Value]").SingleAsync();
        Assert.Contains("SQL Server", version, StringComparison.OrdinalIgnoreCase);
        var discoveredMigrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(discoveredMigrations, appliedMigrations);
        Assert.Contains("20261121190210_InitialCreate", appliedMigrations);
        Assert.Contains("20261123000418_InitialCreate_net10", appliedMigrations);
        Assert.True(Array.IndexOf(appliedMigrations, "20261121190210_InitialCreate") <
                    Array.IndexOf(appliedMigrations, "20261123000418_InitialCreate_net10"));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        _output.WriteLine($"LocalDB database: {_database.DatabaseName}");
        _output.WriteLine($"SQL Server version: {version}");
        _output.WriteLine("All migrations applied cleanly; no migrations or model changes are pending.");
    }

    [Fact]
    public async Task Analytics_index_exists_on_queue_date_and_doctor_with_covering_columns()
    {
        await using var context = CreateContext();
        var keyColumns = await context.Database.SqlQueryRaw<string>("""
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = 'IX_QueueEntries_QueueDate_DoctorId'
            """).SingleAsync();
        var includedColumns = await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
            WHERE i.name = 'IX_QueueEntries_QueueDate_DoctorId'
            """).SingleAsync();

        Assert.Equal("QueueDate,DoctorId", keyColumns);
        Assert.Equal(8, includedColumns);
    }

    [Fact]
    public async Task Account_security_migration_sets_existing_user_flag_to_false()
    {
        const string previousMigration = "20261123020059_AddWalkInAdmissionApprovals";
        const string securityMigration = "20261124015135_AddAccountProvisioningSecurity";
        var userId = Guid.NewGuid();
        await using var context = CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [AspNetUsers]
                    ([Id], [FullName], [IsActive], [CreatedAtUtc], [UserName], [NormalizedUserName], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
                VALUES
                    ({userId}, N'Pre-existing user', 1, {DateTimeOffset.UtcNow}, N'preexisting-security-test', N'PREEXISTING-SECURITY-TEST', 0, 0, 0, 0, 0)
                """);
            await migrator.MigrateAsync(securityMigration);
            var flag = await context.Database.SqlQuery<bool>($"SELECT [MustChangePassword] AS [Value] FROM [AspNetUsers] WHERE [Id] = {userId}").SingleAsync();
            Assert.False(flag);
        }
        finally
        {
            // Restore the latest schema: migrating only to the security migration would leave later migrations rolled back.
            await migrator.MigrateAsync();
        }
    }

    [Fact]
    public async Task Patient_phone_unique_index_rejects_duplicate_numbers()
    {
        var seed = await SeedAsync();
        var secondUserId = await InsertUserAsync();
        await AssertSqlErrorAsync(() => InsertPatientAsync(Guid.NewGuid(), secondUserId, seed.PatientPhoneNumber), 2601, 2627);
        Assert.NotEqual(Guid.Empty, seed.PatientId);
    }

    [Fact]
    public async Task Doctor_provisioning_rolls_back_real_identity_insert_when_profile_creation_fails()
    {
        var specializationId = Guid.NewGuid();
        var specializationName = "Rollback-" + specializationId.ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MediCareDbContext>(options => options.UseSqlServer(_database.ConnectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediCareDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            context.Specializations.Add(new Specialization(specializationId, specializationName));
            await context.SaveChangesAsync();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var roleCreation = await roleManager.CreateAsync(new IdentityRole<Guid>(MediCareRoles.Doctor) { Id = Guid.NewGuid() });
            Assert.True(roleCreation.Succeeded);

            var service = new AccountProvisioningService(
                new IdentityAccountStore(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()),
                new FailAfterIdentityDoctorRepository(new EfAccountProvisioningRepository(context)),
                new EfUnitOfWork(context));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDoctorAsync(
                new CreateDoctorAccountRequest("rollback-doctor@example.invalid", "Rollback Doctor", specializationId, 20), CancellationToken.None));
            Assert.False(await context.Users.AnyAsync(user => user.UserName == "rollback-doctor@example.invalid"));
        }
    }

    [Fact]
    public async Task Production_identity_allows_patients_without_email_and_rejects_duplicate_emails()
    {
        const string password = "Patient-Pa55word!";
        var duplicateEmail = $"dup-{Guid.NewGuid():N}@example.invalid";
        await using var provider = BuildProductionIdentityProvider(_database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await EnsureRolesAsync(services, MediCareRoles.Patient);
        var context = services.GetRequiredService<MediCareDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var store = new IdentityAccountStore(users);
        var service = new AccountProvisioningService(store, new EfAccountProvisioningRepository(context), new EfUnitOfWork(context));

        var receptionPhone = UniqueEgyptianPhone();
        var reception = await service.CreatePatientAsync(new CreatePatientAccountRequest(receptionPhone, "Reception Patient"), CancellationToken.None);
        Assert.True(reception.Succeeded, string.Join(", ", reception.Errors));
        Assert.NotNull(await users.FindByNameAsync(receptionPhone));

        var selfPhone = UniqueEgyptianPhone();
        var selfRegistered = await service.RegisterPatientAsync(new RegisterPatientAccountRequest(selfPhone, "Self Patient", null, password), CancellationToken.None);
        Assert.True(selfRegistered.Succeeded, string.Join(", ", selfRegistered.Errors));
        Assert.NotNull(await users.FindByNameAsync(selfPhone));

        var first = await service.RegisterPatientAsync(new RegisterPatientAccountRequest(UniqueEgyptianPhone(), "First Patient", duplicateEmail, password), CancellationToken.None);
        Assert.True(first.Succeeded, string.Join(", ", first.Errors));

        // Service-level pre-check (case-insensitive via Identity normalization).
        var duplicateViaService = await service.RegisterPatientAsync(new RegisterPatientAccountRequest(UniqueEgyptianPhone(), "Second Patient", duplicateEmail.ToUpperInvariant(), password), CancellationToken.None);
        Assert.False(duplicateViaService.Succeeded);

        // Database-level guard: bypasses the service pre-check and must fail without throwing.
        var duplicateViaStore = await store.CreateAsync(UniqueEgyptianPhone(), duplicateEmail.ToUpperInvariant(), "Second Patient", null, MediCareRoles.Patient, password, false, CancellationToken.None);
        Assert.False(duplicateViaStore.Succeeded);
        Assert.Contains("An account with the provided details already exists.", duplicateViaStore.Errors);
    }

    [Fact]
    public async Task Bootstrap_seeder_creates_admin_once_on_empty_database()
    {
        var isolated = new LocalDbDatabaseFixture();
        await isolated.InitializeAsync();
        try
        {
            var settings = new InitialAdminSettings($"admin-{Guid.NewGuid():N}@example.invalid", "Bootstrap-Pa55word!", "Bootstrap Admin");
            await using var provider = BuildProductionIdentityProvider(isolated.ConnectionString);
            await InitialAdminSeeder.SeedAsync(provider, settings);
            await InitialAdminSeeder.SeedAsync(provider, settings);

            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in new[] { MediCareRoles.Admin, MediCareRoles.Doctor, MediCareRoles.Receptionist, MediCareRoles.Patient })
                Assert.True(await roles.RoleExistsAsync(role), $"Role '{role}' should exist.");

            var admin = await users.FindByEmailAsync(settings.Email);
            Assert.NotNull(admin);
            Assert.True(await users.IsInRoleAsync(admin!, MediCareRoles.Admin));
            Assert.Single(await users.GetUsersInRoleAsync(MediCareRoles.Admin));
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task Bootstrap_seeder_refuses_to_promote_existing_non_admin_with_configured_email()
    {
        var isolated = new LocalDbDatabaseFixture();
        await isolated.InitializeAsync();
        try
        {
            var email = $"patient-{Guid.NewGuid():N}@example.invalid";
            var settings = new InitialAdminSettings(email, "Bootstrap-Pa55word!", "Bootstrap Admin");
            await using var provider = BuildProductionIdentityProvider(isolated.ConnectionString);
            await using (var scope = provider.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                await EnsureRolesAsync(services, MediCareRoles.Admin, MediCareRoles.Doctor, MediCareRoles.Receptionist, MediCareRoles.Patient);
                var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                var patient = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = email,
                    Email = email,
                    FullName = "Existing Patient",
                    EmailConfirmed = true,
                    IsActive = true
                };
                Assert.True((await users.CreateAsync(patient, "Patient-Pa55word!")).Succeeded);
                Assert.True((await users.AddToRoleAsync(patient, MediCareRoles.Patient)).Succeeded);
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InitialAdminSeeder.SeedAsync(provider, settings));
            Assert.Contains("existing non-Admin account", exception.Message);

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyUsers = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await verifyUsers.FindByEmailAsync(email);
            Assert.NotNull(stored);
            Assert.False(await verifyUsers.IsInRoleAsync(stored!, MediCareRoles.Admin));
            Assert.True(await verifyUsers.IsInRoleAsync(stored!, MediCareRoles.Patient));
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    private static ServiceProvider BuildProductionIdentityProvider(string connectionString)
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

    [Fact]
    public async Task Active_slot_index_rejects_duplicate_pending_and_confirmed_and_allows_cancelled_or_rejected_rebooking()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 1, 4);
        var pending = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20));
        await InsertAppointmentAsync(pending);
        await AssertSqlErrorAsync(() => InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 25))), 2601, 2627);

        var confirmed = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 20), new TimeOnly(9, 40));
        confirmed.Confirm(DateTimeOffset.UtcNow);
        await InsertAppointmentAsync(confirmed);
        await AssertSqlErrorAsync(() => InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 20), new TimeOnly(9, 45))), 2601, 2627);

        await ExecuteAsync($"UPDATE Appointments SET Status = 'Cancelled' WHERE Id = {pending.Id}");
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20)));

        var rejected = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 40), new TimeOnly(10, 0));
        rejected.Reject(DateTimeOffset.UtcNow);
        await InsertAppointmentAsync(rejected);
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 40), new TimeOnly(10, 0)));
    }

    [Fact]
    public async Task Rescheduled_from_unique_index_allows_only_one_live_replacement()
    {
        var seed = await SeedAsync();
        var original = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 5), new TimeOnly(9, 0), new TimeOnly(9, 20));
        original.Confirm(DateTimeOffset.UtcNow);
        await InsertAppointmentAsync(original);

        var first = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 6), new TimeOnly(9, 0), new TimeOnly(9, 20), original.Id);
        await InsertAppointmentAsync(first);
        await AssertSqlErrorAsync(() => InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 7), new TimeOnly(9, 0), new TimeOnly(9, 20), original.Id)), 2601, 2627);

        await ExecuteAsync($"UPDATE Appointments SET Status = 'Rejected' WHERE Id = {first.Id}");
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 7), new TimeOnly(9, 0), new TimeOnly(9, 20), original.Id));

        var otherOriginal = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 8), new TimeOnly(9, 0), new TimeOnly(9, 20));
        otherOriginal.Confirm(DateTimeOffset.UtcNow);
        await InsertAppointmentAsync(otherOriginal);
        var cancelled = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 9), new TimeOnly(9, 0), new TimeOnly(9, 20), otherOriginal.Id);
        await InsertAppointmentAsync(cancelled);
        await ExecuteAsync($"UPDATE Appointments SET Status = 'Cancelled' WHERE Id = {cancelled.Id}");
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 10), new TimeOnly(9, 0), new TimeOnly(9, 20), otherOriginal.Id));
    }

    [Fact]
    public async Task Queue_source_appointment_sequence_check_rejects_each_invalid_combination()
    {
        var seed = await SeedAsync();
        var appointment = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 2, 1), new TimeOnly(9, 0), new TimeOnly(9, 20));
        await InsertAppointmentAsync(appointment);
        var date = new DateOnly(2027, 2, 1);
        var arrival = DateTimeOffset.UtcNow;

        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "WalkIn", arrival, 1, "Waiting"), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", arrival, null, "Waiting"), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", arrival, 0, "Waiting"), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "Scheduled", arrival, 1, "Waiting"), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "Scheduled", arrival, null, "Waiting"), "CK_QueueEntries_SourceAppointmentSequence");
    }

    [Fact]
    public async Task Queue_indexes_enforce_one_active_consultation_and_unique_appointment_and_daily_sequence()
    {
        var seed = await SeedAsync();
        var secondDoctorId = (await SeedAsync()).DoctorId;
        var date = new DateOnly(2027, 2, 2);
        var arrival = DateTimeOffset.UtcNow;
        var activeOne = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", arrival, 1, "InConsultation");
        await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", arrival.AddMinutes(1), 2, "InConsultation"), 2601, 2627);
        await InsertQueueRawAsync(seed.PatientId, secondDoctorId, null, date, "WalkIn", arrival, 1, "InConsultation");

        var appointment = NewAppointment(seed.PatientId, seed.DoctorId, date.AddDays(1), new TimeOnly(9, 0), new TimeOnly(9, 20));
        await InsertAppointmentAsync(appointment);
        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date.AddDays(1), "Scheduled", arrival, null, "Waiting");
        await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date.AddDays(1), "Scheduled", arrival.AddMinutes(1), null, "Waiting"), 2601, 2627);

        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", arrival.AddMinutes(2), 10, "Waiting");
        await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", arrival.AddMinutes(3), 10, "Waiting"), 2601, 2627);
        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date.AddDays(1), "WalkIn", arrival.AddMinutes(4), 10, "Waiting");
    }

    [Fact]
    public async Task Appointment_queue_index_allows_history_attempts_but_one_live_attempt_per_appointment()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 2, 3);
        var appointment = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20));
        await InsertAppointmentAsync(appointment);
        var arrival = DateTimeOffset.UtcNow;

        try
        {
            // Cancelled and NoShow attempts are history and do not occupy the live slot.
            await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "Scheduled", arrival, null, "Cancelled");
            await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "Scheduled", arrival.AddMinutes(1), null, "Cancelled");
            await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "Scheduled", arrival.AddMinutes(2), null, "NoShow");

            // One live attempt is allowed; a second live attempt for the same appointment is rejected.
            await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "Scheduled", arrival.AddMinutes(3), null, "Waiting");
            await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "Scheduled", arrival.AddMinutes(4), null, "Called"), 2601, 2627);
            await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, "Scheduled", arrival.AddMinutes(5), null, "Waiting"), 2601, 2627);
        }
        finally
        {
            // Duplicate attempts would block the rollback of the narrowed index in the migration tests, so remove them.
            await ExecuteAsync($"DELETE FROM QueueEntries WHERE AppointmentId = {appointment.Id}");
        }
    }

    [Fact]
    public async Task Doctor_and_patient_account_links_are_unique()
    {
        var seed = await SeedAsync();
        var anotherSpecializationId = await InsertSpecializationAsync("Cardiology-" + Guid.NewGuid().ToString("N"));

        await AssertSqlErrorAsync(() => InsertDoctorAsync(Guid.NewGuid(), seed.UserId, anotherSpecializationId), 2601, 2627);
        await AssertSqlErrorAsync(() => InsertPatientAsync(Guid.NewGuid(), seed.UserId), 2601, 2627);
    }

    [Fact]
    public async Task Duration_and_time_date_ranges_are_enforced_by_checks()
    {
        var seed = await SeedAsync();
        var userId = await InsertUserAsync();
        await AssertSqlConstraintErrorAsync(() => InsertDoctorRawAsync(Guid.NewGuid(), userId, seed.SpecializationId, 0), "CK_Doctors_AppointmentDurationPositive");
        await AssertSqlConstraintErrorAsync(() => InsertAppointmentRawAsync(seed.PatientId, seed.DoctorId, new DateOnly(2027, 3, 1), new TimeOnly(9, 30), new TimeOnly(9, 30)), "CK_Appointments_TimeRange");
        await AssertSqlConstraintErrorAsync(() => InsertBreakRawAsync(seed.DoctorId, new DateOnly(2027, 3, 1), new TimeOnly(10, 0), new TimeOnly(9, 0)), "CK_DoctorBreaks_TimeRange");
        await AssertSqlConstraintErrorAsync(() => InsertWorkingHourRawAsync(seed.DoctorId, new TimeOnly(10, 0), new TimeOnly(9, 0)), "CK_DoctorWorkingHours_TimeRange");
        await AssertSqlConstraintErrorAsync(() => InsertHolidayRawAsync(seed.DoctorId, new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 1)), "CK_DoctorHolidays_DateRange");
    }

    [Fact]
    public async Task No_action_relationships_and_specialization_name_uniqueness_are_enforced()
    {
        var seed = await SeedAsync();
        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, seed.AppointmentId, new DateOnly(2027, 3, 4), "Scheduled", DateTimeOffset.UtcNow, null, "Waiting");
        await AssertSqlErrorAsync(() => DeleteAsync("Doctors", seed.DoctorId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("Patients", seed.PatientId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("Appointments", seed.AppointmentId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("Specializations", seed.SpecializationId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("AspNetUsers", seed.UserId), 547);
        await AssertSqlErrorAsync(() => InsertSpecializationAsync(seed.SpecializationName), 2601, 2627);
    }

    [Fact]
    public async Task Concurrent_application_bookings_for_one_slot_yield_exactly_one_success()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 3, 8);
        var outcomes = await Task.WhenAll(
            TryBookAsync(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 20)),
            TryBookAsync(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 20)));

        Assert.Equal(1, outcomes.Count(result => result));
        Assert.Equal(1, outcomes.Count(result => !result));
    }

    [Fact]
    public async Task Concurrent_walk_ins_receive_unique_sequences_and_fifo_order()
    {
        const int count = 12;
        var seed = await SeedAsync();
        var queueDate = new DateOnly(2027, 3, 9);
        var patientIds = new Guid[count];
        await using (var context = CreateContext())
        {
            for (var i = 0; i < count; i++)
            {
                var userId = Guid.NewGuid();
                patientIds[i] = Guid.NewGuid();
                context.Users.Add(new ApplicationUser { Id = userId, UserName = "walkin" + userId.ToString("N"), FullName = "Concurrent Patient" });
                context.Patients.Add(new Patient(patientIds[i], userId, "Concurrent Patient " + i, "01000000" + i.ToString("D3")));
            }
            await context.SaveChangesAsync();
        }

        var registeredIds = await Task.WhenAll(patientIds.Select(patientId => RegisterWalkInAsync(patientId, seed.DoctorId, queueDate)));

        await using var verify = CreateContext();
        var registered = await verify.QueueEntries.Where(entry => registeredIds.Contains(entry.Id)).ToArrayAsync();
        Assert.Equal(count, registered.Length);
        Assert.Equal(count, registered.Select(entry => entry.QueueSequence).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, count).Select(value => (long)value), registered.Select(entry => entry.QueueSequence!.Value).Order());

        var repositoryOrder = await new EfQueueRepository(verify).GetWalkInEntriesForDoctorAndDateAsync(seed.DoctorId, queueDate, CancellationToken.None);
        var expectedOrder = registered.OrderBy(entry => entry.ArrivedAt).ThenBy(entry => entry.QueueSequence).Select(entry => entry.Id);
        Assert.Equal(expectedOrder, repositoryOrder.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Concurrent_admission_requests_for_one_entry_create_one_pending_request()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 10);
        var arrivedAt = new DateTimeOffset(2027, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, arrivedAt, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var outcomes = await Task.WhenAll(
            TryRequestAdmissionAsync(entry.Id),
            TryRequestAdmissionAsync(entry.Id));

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.WalkInAdmissionApprovals.CountAsync(approval => approval.QueueEntryId == entry.Id && approval.Decision == WalkInAdmissionDecision.Pending));
    }

    [Fact]
    public async Task Concurrent_approve_and_reject_for_one_request_allow_one_decider()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 3, 10);
        var now = new DateTimeOffset(2027, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, now.AddMinutes(-5), 1);
        var approval = WalkInAdmissionApproval.Request(entry, now.AddMinutes(-2), now.AddMinutes(30));
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            context.WalkInAdmissionApprovals.Add(approval);
            await context.SaveChangesAsync();
        }

        var outcomes = await Task.WhenAll(
            TryDecideAdmissionAsync(approval.Id, seed.UserId, approve: true),
            TryDecideAdmissionAsync(approval.Id, seed.UserId, approve: false));

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        await using var verify = CreateContext();
        var storedApproval = await verify.WalkInAdmissionApprovals.SingleAsync(item => item.Id == approval.Id);
        var storedEntry = await verify.QueueEntries.SingleAsync(item => item.Id == entry.Id);
        Assert.Contains(storedApproval.Decision, new[] { WalkInAdmissionDecision.Approved, WalkInAdmissionDecision.Rejected });
        Assert.Equal(QueueEntryStatus.Waiting, storedEntry.Status);
    }

    [Fact]
    public async Task Concurrent_calls_for_one_doctor_allow_only_one_entry_to_be_called()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 3, 10);
        var now = new DateTimeOffset(2027, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var secondUserId = await InsertUserAsync();
        var secondPatientId = Guid.NewGuid();
        await InsertPatientAsync(secondPatientId, secondUserId, "+2010" + Guid.NewGuid().ToString("N")[..8]);
        var first = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, now.AddMinutes(-10), 1);
        var second = QueueEntry.CreateWalkIn(secondPatientId, seed.DoctorId, date, now.AddMinutes(-9), 2);
        var firstApproval = WalkInAdmissionApproval.Request(first, now.AddMinutes(-5), now.AddMinutes(30));
        var secondApproval = WalkInAdmissionApproval.Request(second, now.AddMinutes(-5), now.AddMinutes(30));
        firstApproval.Approve(now.AddMinutes(-2));
        secondApproval.Approve(now.AddMinutes(-2));
        await using (var context = CreateContext())
        {
            context.QueueEntries.AddRange(first, second);
            context.WalkInAdmissionApprovals.AddRange(firstApproval, secondApproval);
            await context.SaveChangesAsync();
        }

        var outcomes = await Task.WhenAll(
            TryCallAdmissionAsync(first.Id, firstApproval.Id, date, now),
            TryCallAdmissionAsync(second.Id, secondApproval.Id, date, now));

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.QueueEntries.CountAsync(item => item.DoctorId == seed.DoctorId && item.QueueDate == date && item.Status == QueueEntryStatus.Called));
        Assert.Equal(1, await verify.WalkInAdmissionApprovals.CountAsync(item =>
            (item.Id == firstApproval.Id || item.Id == secondApproval.Id) && item.UsedAtUtc != null));
    }

    [Fact]
    public async Task Walk_in_admission_decision_check_rejects_invalid_values_and_timestamp_combinations()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 11);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, DateTimeOffset.UtcNow, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var requested = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expires = DateTimeOffset.UtcNow.AddMinutes(20);
        var decided = DateTimeOffset.UtcNow.AddMinutes(-5);
        var used = DateTimeOffset.UtcNow.AddMinutes(-2);

        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Aproved", requested, expires, decided, null), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Pending", requested, expires, null, used), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Rejected", requested, expires, decided, used), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Expired", requested, expires, decided, used), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Approved", requested, expires, null, null), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Approved", requested, expires, decided, decided.AddTicks(-1)), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
    }

    [Fact]
    public async Task Walk_in_admission_decision_check_allows_valid_decisions_and_optional_approval_use()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 12);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, DateTimeOffset.UtcNow, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var requested = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expires = DateTimeOffset.UtcNow.AddMinutes(20);
        var decided = DateTimeOffset.UtcNow.AddMinutes(-5);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Pending", requested, expires, null, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Approved", requested, expires, decided, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Approved", requested, expires, decided, decided);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Rejected", requested, expires, decided, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Expired", requested, expires, decided, null);
    }

    [Fact]
    public async Task Walk_in_admission_pending_index_allows_rejected_history_but_rejects_second_pending()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 13);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, DateTimeOffset.UtcNow, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var requested = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expires = DateTimeOffset.UtcNow.AddMinutes(20);
        var decided = DateTimeOffset.UtcNow.AddMinutes(-5);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Rejected", requested, expires, decided, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Pending", requested, expires, null, null);
        await AssertSqlErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, "Pending", requested, expires, null, null), 2601, 2627);
    }

    [Fact]
    public async Task Queue_repository_orders_scheduled_by_appointment_time_and_walk_ins_by_arrival_then_sequence()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 10);
        var appointmentAtTen = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(10, 0), new TimeOnly(10, 20));
        var appointmentAtNine = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20));
        appointmentAtTen.Confirm(DateTimeOffset.UtcNow);
        appointmentAtNine.Confirm(DateTimeOffset.UtcNow);
        var baseArrival = new DateTimeOffset(2027, 3, 10, 6, 0, 0, TimeSpan.Zero);
        var laterArrivalHigherSeq = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, baseArrival.AddMinutes(1), 2);
        var earlyArrivalHigherSeq = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, baseArrival, 3);
        var laterArrivalLowerSeq = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, baseArrival.AddMinutes(1), 1);
        await using (var context = CreateContext())
        {
            context.Appointments.AddRange(appointmentAtTen, appointmentAtNine);
            context.QueueEntries.AddRange(
                QueueEntry.CreateScheduled(seed.PatientId, seed.DoctorId, appointmentAtTen.Id, date, baseArrival),
                QueueEntry.CreateScheduled(seed.PatientId, seed.DoctorId, appointmentAtNine.Id, date, baseArrival.AddMinutes(1)),
                laterArrivalHigherSeq, earlyArrivalHigherSeq, laterArrivalLowerSeq);
            await context.SaveChangesAsync();
        }

        await using var queryContext = CreateContext();
        var repository = new EfQueueRepository(queryContext);
        var scheduled = await repository.GetScheduledEntriesForDoctorAndDateAsync(seed.DoctorId, date, CancellationToken.None);
        var walkIns = await repository.GetWalkInEntriesForDoctorAndDateAsync(seed.DoctorId, date, CancellationToken.None);

        Assert.Equal(new[] { appointmentAtNine.Id, appointmentAtTen.Id }, scheduled.Select(entry => entry.AppointmentId!.Value));
        Assert.Equal(new[] { earlyArrivalHigherSeq.Id, laterArrivalLowerSeq.Id, laterArrivalHigherSeq.Id }, walkIns.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Working_hour_extension_checks_reject_invalid_times_statuses_and_decision_timestamps()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 2, 7);

        await AssertSqlConstraintErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:00", "Pending", null),
            "CK_DoctorWorkingHourExtensions_TimeRange");
        // 'Expired' is not a stored status; both the status and the decision-timestamp checks reject it, so match the table's checks.
        await AssertSqlConstraintErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Expired", null),
            "CK_DoctorWorkingHourExtensions_");
        await AssertSqlConstraintErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Pending", DateTimeOffset.UtcNow),
            "CK_DoctorWorkingHourExtensions_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Approved", null),
            "CK_DoctorWorkingHourExtensions_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Approved", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow),
            "CK_DoctorWorkingHourExtensions_DecidedAfterRequest");
    }

    [Fact]
    public async Task Working_hour_extension_live_index_allows_history_but_one_pending_or_approved_per_window()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 2, 8);

        await InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Rejected", DateTimeOffset.UtcNow);
        await InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Cancelled", DateTimeOffset.UtcNow);
        await InsertExtensionRawAsync(seed, date, "11:00", "11:30", "Pending", null);
        await AssertSqlErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:40", "Pending", null), 2601, 2627);
        await AssertSqlErrorAsync(() => InsertExtensionRawAsync(seed, date, "11:00", "11:40", "Approved", DateTimeOffset.UtcNow), 2601, 2627);

        await InsertExtensionRawAsync(seed, date, "10:00", "10:30", "Approved", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Working_hour_extension_requires_an_existing_doctor()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var orphan = seed with { DoctorId = Guid.NewGuid() };

        await AssertSqlConstraintErrorAsync(() => InsertExtensionRawAsync(orphan, new DateOnly(2027, 2, 9), "11:00", "11:30", "Pending", null),
            "FK_DoctorWorkingHourExtensions_Doctors_DoctorId");
    }

    [Fact]
    public async Task Availability_reads_an_approved_extension_from_the_database_and_ignores_a_pending_one()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var approvedDate = new DateOnly(2027, 2, 10);
        var pendingDate = new DateOnly(2027, 2, 11);
        await InsertExtensionRawAsync(seed, approvedDate, "11:00", "11:40", "Approved", DateTimeOffset.UtcNow);
        await InsertExtensionRawAsync(seed, pendingDate, "11:00", "11:40", "Pending", null);

        await using var context = CreateContext();
        var appointments = new EfAppointmentRepository(context);
        var availability = new DoctorAvailabilityService(appointments, new EfDoctorScheduleRepository(context),
            new FixedClinicClock(approvedDate.AddDays(-1)));

        var approvedWindow = Assert.Single(await availability.GetEffectiveWindowsAsync(seed.DoctorId, approvedDate, CancellationToken.None));
        var approvedSlots = await availability.GetAvailableSlotsAsync(seed.DoctorId, approvedDate, CancellationToken.None);
        var pendingWindow = Assert.Single(await availability.GetEffectiveWindowsAsync(seed.DoctorId, pendingDate, CancellationToken.None));

        Assert.Equal(new TimeOnly(11, 40), approvedWindow.EndTime);
        Assert.Contains(new TimeOnly(11, 20), approvedSlots);
        Assert.Equal(new TimeOnly(11, 0), pendingWindow.EndTime);
    }

    /// <summary>Fixed record time so day-end timestamps can be placed deterministically before or after it.</summary>
    [Fact]
    public async Task Medical_records_enforce_one_record_per_attempt_and_one_prescription_per_record()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 6, 2);
        var entryId = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", DateTimeOffset.UtcNow, 8001, "InConsultation");
        var recordId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertMedicalRecordRawAsync(recordId, seed.PatientId, seed.DoctorId, entryId, date, "Flu", now);

        // One record per attendance attempt.
        await AssertSqlErrorAsync(() => InsertMedicalRecordRawAsync(Guid.NewGuid(), seed.PatientId, seed.DoctorId, entryId, date, "Flu", now), 2601, 2627);
        // A record must point at a real attempt. The check cases use a real spare attempt so only the check can fail.
        var spareEntryId = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", DateTimeOffset.UtcNow, 8002, "Waiting");
        await AssertSqlConstraintErrorAsync(() => InsertMedicalRecordRawAsync(Guid.NewGuid(), seed.PatientId, seed.DoctorId, Guid.NewGuid(), date, "Flu", now),
            "FK_MedicalRecords_QueueEntries_QueueEntryId");
        await AssertSqlConstraintErrorAsync(() => InsertMedicalRecordRawAsync(Guid.NewGuid(), seed.PatientId, seed.DoctorId, spareEntryId, date, "   ", now),
            "CK_MedicalRecords_DiagnosisRequired");
        await AssertSqlConstraintErrorAsync(() => InsertMedicalRecordRawAsync(Guid.NewGuid(), seed.PatientId, seed.DoctorId, spareEntryId, date, "Flu", now, now.AddMinutes(-1)),
            "CK_MedicalRecords_UpdatedAfterCreated");

        // One prescription per record.
        var prescriptionId = Guid.NewGuid();
        await InsertPrescriptionRawAsync(prescriptionId, recordId);
        await AssertSqlErrorAsync(() => InsertPrescriptionRawAsync(Guid.NewGuid(), recordId), 2601, 2627);

        // Items: positive duration and non-blank required text.
        await InsertPrescriptionItemRawAsync(Guid.NewGuid(), prescriptionId, "Amoxicillin", 5);
        await AssertSqlConstraintErrorAsync(() => InsertPrescriptionItemRawAsync(Guid.NewGuid(), prescriptionId, "Amoxicillin", 0), "CK_PrescriptionItems_DurationPositive");
        await AssertSqlConstraintErrorAsync(() => InsertPrescriptionItemRawAsync(Guid.NewGuid(), prescriptionId, "  ", 3), "CK_PrescriptionItems_TextRequired");
        await AssertSqlConstraintErrorAsync(() => InsertPrescriptionItemRawAsync(Guid.NewGuid(), Guid.NewGuid(), "Amoxicillin", 3),
            "FK_PrescriptionItems_Prescriptions_PrescriptionId");

        // Addenda are non-blank and tied to a record and a doctor.
        await AssertSqlConstraintErrorAsync(() => InsertAddendumRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "   "), "CK_MedicalRecordAddenda_TextRequired");
        await AssertSqlConstraintErrorAsync(() => InsertAddendumRawAsync(Guid.NewGuid(), Guid.NewGuid(), seed.DoctorId, "Lab result"), "FK_MedicalRecordAddenda_MedicalRecords_MedicalRecordId");
        await InsertAddendumRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "Lab result received");
    }

    /// <summary>
    /// Regression (found in the browser): children added through a tracked record's navigations were sent as
    /// UPDATEs of non-existent rows because their client-generated Guid keys looked like existing keys.
    /// </summary>
    [Fact]
    public async Task Medical_record_children_added_through_navigations_are_inserted_and_prescription_items_can_be_replaced()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 6, 3);
        var entryId = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", DateTimeOffset.UtcNow, 8101, "InConsultation");
        var recordId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertMedicalRecordRawAsync(recordId, seed.PatientId, seed.DoctorId, entryId, date, "Flu", now);

        await using (var context = CreateContext())
        {
            var record = await new EfMedicalRecordRepository(context).GetRecordByIdAsync(recordId, CancellationToken.None);
            record!.SetPrescription(seed.DoctorId, date,
                [new("Aspirin", "75 mg", "Once daily", 30, null), new("Paracetamol", "500 mg", "Twice daily", 5, null)], null, now);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var record = await new EfMedicalRecordRepository(context).GetRecordByIdAsync(recordId, CancellationToken.None);
            record!.SetPrescription(seed.DoctorId, date, [new("Aspirin", "100 mg", "Once daily", 30, "After food")], "Replaced", now.AddMinutes(1));
            record.AddAddendum(seed.DoctorId, date.AddDays(1), "Lab result normal", now.AddDays(1));
            record.AddAttachment(MedicalAttachment.Create(recordId, seed.DoctorId, "ecg.pdf", "application/pdf", 10,
                Guid.NewGuid().ToString("N") + ".pdf", new string('a', 64), null, now.AddDays(1)));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var prescription = await context.Set<Prescription>().Include(item => item.Items).SingleAsync(item => item.MedicalRecordId == recordId);
            var item = Assert.Single(prescription.Items);
            Assert.Equal("100 mg", item.Dosage);
            Assert.Equal("Replaced", prescription.Notes);
            Assert.Equal(1, await context.Set<MedicalRecordAddendum>().CountAsync(addendum => addendum.MedicalRecordId == recordId));
            Assert.Equal(1, await context.Set<MedicalAttachment>().CountAsync(attachment => attachment.MedicalRecordId == recordId));
        }
    }

    [Fact]
    public async Task Medical_attachments_enforce_type_size_digest_names_and_unique_stored_names()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 6, 2);
        var entryId = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", DateTimeOffset.UtcNow, 8101, "InConsultation");
        var recordId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertMedicalRecordRawAsync(recordId, seed.PatientId, seed.DoctorId, entryId, date, "Flu", now);
        var digest = new string('a', 64);

        var stored = "0123456789abcdef0123456789abcdef.pdf";
        await InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "lab.pdf", "application/pdf", 100, stored, digest, null, now);

        // One stored name is never reused.
        await AssertSqlErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "lab.pdf", "application/pdf", 100, stored, digest, null, now), 2601, 2627);

        // Each check is exercised with otherwise valid values so only that check can fail.
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "x.exe", "application/x-msdownload", 100,
            "1111111111111111111111111111aaaa.pdf", digest, null, now), "CK_MedicalAttachments_ContentType");
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "x.pdf", "application/pdf", 0,
            "2222222222222222222222222222aaaa.pdf", digest, null, now), "CK_MedicalAttachments_SizePositive");
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "x.pdf", "application/pdf", 100,
            "3333333333333333333333333333aaaa.pdf", "abc", null, now), "CK_MedicalAttachments_Sha256Length");
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "x.pdf", "application/pdf", 100,
            "../evil.pdf", digest, null, now), "CK_MedicalAttachments_StoredFileNameLength");
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "   ", "application/pdf", 100,
            "4444444444444444444444444444aaaa.pdf", digest, null, now), "CK_MedicalAttachments_OriginalFileNameRequired");
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), recordId, seed.DoctorId, "x.pdf", "application/pdf", 100,
            "5555555555555555555555555555aaaa.pdf", digest, "   ", now), "CK_MedicalAttachments_DescriptionNotBlank");
        await AssertSqlConstraintErrorAsync(() => InsertAttachmentRawAsync(Guid.NewGuid(), Guid.NewGuid(), seed.DoctorId, "x.pdf", "application/pdf", 100,
            "6666666666666666666666666666aaaa.pdf", digest, null, now), "FK_MedicalAttachments_MedicalRecords_MedicalRecordId");
    }

    [Fact]
    public async Task Notifications_enforce_text_related_pair_and_one_dedup_key_per_recipient()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var key = "reminder:" + Guid.NewGuid().ToString("D");

        await InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "AppointmentReminder", "Tomorrow", "Reminder.", "Appointment", Guid.NewGuid(), key, now, null);

        // One dedup key per recipient, while the same key may go to another recipient.
        await AssertSqlErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "AppointmentReminder", "Tomorrow", "Reminder.", "Appointment", Guid.NewGuid(), key, now, null), 2601, 2627);
        await InsertNotificationRawAsync(Guid.NewGuid(), other.UserId, "AppointmentReminder", "Tomorrow", "Reminder.", "Appointment", Guid.NewGuid(), key, now, null);

        // Rows without a dedup key do not conflict with each other.
        await InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "BookingRequestConfirmed", "Confirmed", "Booked.", null, null, null, now, null);
        await InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "BookingRequestConfirmed", "Confirmed", "Booked.", null, null, null, now, now);

        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "", "Title", "Message", null, null, null, now, null), "CK_Notifications_KindRequired");
        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "K", "  ", "Message", null, null, null, now, null), "CK_Notifications_TitleRequired");
        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "K", "Title", "", null, null, null, now, null), "CK_Notifications_MessageRequired");
        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "K", "Title", "Message", null, Guid.NewGuid(), null, now, null), "CK_Notifications_RelatedPair");
        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "K", "Title", "Message", "Appointment", null, null, now, null), "CK_Notifications_RelatedPair");
        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), seed.UserId, "K", "Title", "Message", null, null, "   ", now, null), "CK_Notifications_DedupKeyNotBlank");
        await AssertSqlConstraintErrorAsync(() => InsertNotificationRawAsync(Guid.NewGuid(), Guid.NewGuid(), "K", "Title", "Message", null, null, null, now, null), "FK_Notifications_AspNetUsers_RecipientUserId");
    }

    private Task InsertAttachmentRawAsync(Guid id, Guid recordId, Guid doctorId, string originalName, string contentType, long sizeBytes, string storedName, string sha256, string? description, DateTimeOffset uploadedAt) =>
        ExecuteAsync($"INSERT INTO MedicalAttachments (Id, MedicalRecordId, OriginalFileName, ContentType, SizeBytes, StoredFileName, Sha256, Description, UploadedByDoctorId, UploadedAtUtc) VALUES ({id}, {recordId}, {originalName}, {contentType}, {sizeBytes}, {storedName}, {sha256}, {description}, {doctorId}, {uploadedAt})");

    private Task InsertNotificationRawAsync(Guid id, Guid recipientUserId, string kind, string title, string message, string? relatedType, Guid? relatedId, string? dedupKey, DateTimeOffset createdAt, DateTimeOffset? readAt) =>
        ExecuteAsync($"INSERT INTO Notifications (Id, RecipientUserId, Kind, Title, Message, RelatedEntityType, RelatedEntityId, DedupKey, CreatedAtUtc, ReadAtUtc) VALUES ({id}, {recipientUserId}, {kind}, {title}, {message}, {relatedType}, {relatedId}, {dedupKey}, {createdAt}, {readAt})");

    private Task InsertMedicalRecordRawAsync(Guid id, Guid patientId, Guid doctorId, Guid queueEntryId, DateOnly visitDate, string? diagnosis, DateTimeOffset createdAt, DateTimeOffset? updatedAt = null) =>
        ExecuteAsync($"INSERT INTO MedicalRecords (Id, PatientId, DoctorId, QueueEntryId, AppointmentId, VisitDate, ChiefComplaint, Diagnosis, ClinicalNotes, CreatedAtUtc, UpdatedAtUtc) VALUES ({id}, {patientId}, {doctorId}, {queueEntryId}, NULL, {visitDate}, NULL, {diagnosis}, NULL, {createdAt}, {updatedAt})");

    private Task InsertPrescriptionRawAsync(Guid id, Guid recordId) =>
        ExecuteAsync($"INSERT INTO Prescriptions (Id, MedicalRecordId, IssuedAtUtc, UpdatedAtUtc, Notes) VALUES ({id}, {recordId}, {DateTimeOffset.UtcNow}, NULL, NULL)");

    private Task InsertPrescriptionItemRawAsync(Guid id, Guid prescriptionId, string medicationName, int durationDays) =>
        ExecuteAsync($"INSERT INTO PrescriptionItems (Id, PrescriptionId, MedicationName, Dosage, Frequency, DurationDays, Instructions) VALUES ({id}, {prescriptionId}, {medicationName}, '500 mg', 'Twice daily', {durationDays}, NULL)");

    private Task InsertAddendumRawAsync(Guid id, Guid recordId, Guid doctorId, string text) =>
        ExecuteAsync($"INSERT INTO MedicalRecordAddenda (Id, MedicalRecordId, DoctorId, Text, CreatedAtUtc) VALUES ({id}, {recordId}, {doctorId}, {text}, {DateTimeOffset.UtcNow})");

    private static readonly DateTimeOffset DisruptionRecordedAt = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);

    private Task InsertDisruptionRawAsync(Guid id, Guid doctorId, DateOnly date, TimeOnly fromTime, string reason, Guid recordedBy, DateTimeOffset? dayEndAt, Guid? dayEndBy)
    {
        var recordedAt = DisruptionRecordedAt;
        return ExecuteAsync($"INSERT INTO DoctorDisruptions (Id, DoctorId, Date, FromTime, Reason, RecordedByUserId, RecordedAtUtc, DayEndRecordedAtUtc, DayEndRecordedByUserId) VALUES ({id}, {doctorId}, {date}, {fromTime}, {reason}, {recordedBy}, {recordedAt}, {dayEndAt}, {dayEndBy})");
    }

    private Task InsertWorklistItemRawAsync(Guid id, Guid disruptionId, Guid patientId, Guid? appointmentId, Guid? queueEntryId, string outcome, Guid? contactedBy, DateTimeOffset? contactedAt)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        return ExecuteAsync($"INSERT INTO DisruptionWorklistItems (Id, DisruptionId, PatientId, AppointmentId, QueueEntryId, Outcome, LastContactedByUserId, LastContactedAtUtc, Notes, UpdatedAtUtc) VALUES ({id}, {disruptionId}, {patientId}, {appointmentId}, {queueEntryId}, {outcome}, {contactedBy}, {contactedAt}, NULL, {updatedAt})");
    }

    private Task InsertAttemptRawAsync(Guid worklistItemId, Guid contactedBy, string outcome)
    {
        var contactedAt = DateTimeOffset.UtcNow;
        return ExecuteAsync($"INSERT INTO DisruptionContactAttempts (Id, WorklistItemId, ContactedByUserId, ContactedAtUtc, Notes, Outcome) VALUES ({Guid.NewGuid()}, {worklistItemId}, {contactedBy}, {contactedAt}, NULL, {outcome})");
    }

    private Task InsertBreakWithDisruptionRawAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid disruptionId) =>
        ExecuteAsync($"INSERT INTO DoctorBreaks (Id, DoctorId, Date, StartTime, EndTime, DisruptionId) VALUES ({Guid.NewGuid()}, {doctorId}, {date}, {start}, {end}, {disruptionId})");

    private Task InsertExtensionRawAsync(SeededRecords seed, DateOnly date, string originalEnd, string extendedEnd, string status, DateTimeOffset? decidedAt, DateTimeOffset? requestedAt = null) =>
        ExecuteAsync($"""
            INSERT INTO [DoctorWorkingHourExtensions]
                ([Id], [DoctorId], [Date], [OriginalEndTime], [ExtendedEndTime], [Reason], [Status], [RequestedByUserId], [RequestedAtUtc], [DecidedAtUtc], [DecisionNotes])
            VALUES
                ({Guid.NewGuid()}, {seed.DoctorId}, CAST({date.ToString("yyyy-MM-dd")} AS date), CAST({originalEnd} AS time), CAST({extendedEnd} AS time),
                 N'Overflow clinic', {status}, {seed.UserId}, {requestedAt ?? DateTimeOffset.UtcNow.AddDays(-1)}, {decidedAt}, NULL)
            """);

    private MediCareDbContext CreateContext() => _database.CreateContext();

    private static Appointment NewAppointment(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? originalId = null) =>
        Appointment.Request(patientId, doctorId, date, start, end, DateTimeOffset.UtcNow, originalId);

    [Fact]
    public async Task Doctor_disruption_checks_and_one_record_per_doctor_and_date_are_enforced()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 4, 1);
        await InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date, new TimeOnly(11, 0), "Emergency", seed.UserId, null, null);

        await AssertSqlErrorAsync(() => InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date, new TimeOnly(12, 0), "Second", seed.UserId, null, null), 2601, 2627);
        await AssertSqlConstraintErrorAsync(() => InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date.AddDays(1), new TimeOnly(11, 0), "   ", seed.UserId, null, null),
            "CK_DoctorDisruptions_ReasonRequired");
        await AssertSqlConstraintErrorAsync(() => InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date.AddDays(1), new TimeOnly(11, 0), "Emergency", seed.UserId,
                DisruptionRecordedAt.AddHours(1), null),
            "CK_DoctorDisruptions_DayEndPair");
        await AssertSqlConstraintErrorAsync(() => InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date.AddDays(1), new TimeOnly(11, 0), "Emergency", seed.UserId,
                null, seed.UserId),
            "CK_DoctorDisruptions_DayEndPair");
        await AssertSqlConstraintErrorAsync(() => InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date.AddDays(1), new TimeOnly(11, 0), "Emergency", seed.UserId,
                DisruptionRecordedAt.AddHours(-1), seed.UserId),
            "CK_DoctorDisruptions_DayEndAfterRecord");
        // A complete, ordered day end is accepted.
        await InsertDisruptionRawAsync(Guid.NewGuid(), seed.DoctorId, date.AddDays(1), new TimeOnly(11, 0), "Emergency", seed.UserId,
            DisruptionRecordedAt.AddHours(2), seed.UserId);
    }

    [Fact]
    public async Task Disruption_worklist_checks_outcome_contact_pair_and_one_item_per_appointment_or_entry()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 4, 2);
        var disruptionId = Guid.NewGuid();
        await InsertDisruptionRawAsync(disruptionId, seed.DoctorId, date, new TimeOnly(11, 0), "Emergency", seed.UserId, null, null);
        await InsertWorklistItemRawAsync(Guid.NewGuid(), disruptionId, seed.PatientId, seed.AppointmentId, null, "Pending", null, null);

        // Constraint cases use a second disruption so the unique index on (disruption, appointment) cannot mask them.
        var checkDisruptionId = Guid.NewGuid();
        await InsertDisruptionRawAsync(checkDisruptionId, seed.DoctorId, date.AddDays(1), new TimeOnly(11, 0), "Emergency", seed.UserId, null, null);
        await AssertSqlConstraintErrorAsync(() => InsertWorklistItemRawAsync(Guid.NewGuid(), checkDisruptionId, seed.PatientId, null, null, "Pending", null, null),
            "CK_DisruptionWorklistItems_Link");
        await AssertSqlConstraintErrorAsync(() => InsertWorklistItemRawAsync(Guid.NewGuid(), checkDisruptionId, seed.PatientId, null, Guid.NewGuid(), "Pending", null, null),
            "FK_DisruptionWorklistItems_QueueEntries_QueueEntryId");
        await AssertSqlConstraintErrorAsync(() => InsertWorklistItemRawAsync(Guid.NewGuid(), checkDisruptionId, seed.PatientId, seed.AppointmentId, null, "Done", null, null),
            "CK_DisruptionWorklistItems_Outcome");
        await AssertSqlConstraintErrorAsync(() => InsertWorklistItemRawAsync(Guid.NewGuid(), checkDisruptionId, seed.PatientId, seed.AppointmentId, null, "Contacted", seed.UserId, null),
            "CK_DisruptionWorklistItems_ContactPair");
        await InsertWorklistItemRawAsync(Guid.NewGuid(), checkDisruptionId, seed.PatientId, seed.AppointmentId, null, "Pending", null, null);
        await AssertSqlErrorAsync(() => InsertWorklistItemRawAsync(Guid.NewGuid(), checkDisruptionId, seed.PatientId, seed.AppointmentId, null, "Pending", null, null), 2601, 2627);

        // One item per queue entry per disruption.
        var walkInEntry = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, "WalkIn", DateTimeOffset.UtcNow, 7001, "Waiting");
        await InsertWorklistItemRawAsync(Guid.NewGuid(), disruptionId, seed.PatientId, null, walkInEntry, "Pending", null, null);
        await AssertSqlErrorAsync(() => InsertWorklistItemRawAsync(Guid.NewGuid(), disruptionId, seed.PatientId, null, walkInEntry, "Pending", null, null), 2601, 2627);
    }

    [Fact]
    public async Task Worklist_closed_outcomes_need_a_resolution_time_and_automatic_closures_need_a_note()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 4, 9);
        var disruptionId = Guid.NewGuid();
        await InsertDisruptionRawAsync(disruptionId, seed.DoctorId, date, new TimeOnly(11, 0), "Emergency", seed.UserId, null, null);
        var now = DateTimeOffset.UtcNow;

        await AssertSqlConstraintErrorAsync(() => InsertResolvedItemRawAsync(Guid.NewGuid(), disruptionId, seed.PatientId, seed.AppointmentId, "Cancelled", null, null),
            "CK_DisruptionWorklistItems_ClosedOutcomeResolved");
        await AssertSqlConstraintErrorAsync(() => InsertResolvedItemRawAsync(Guid.NewGuid(), disruptionId, seed.PatientId, seed.AppointmentId, "Pending", now, null),
            "CK_DisruptionWorklistItems_ResolutionNote");
        await InsertResolvedItemRawAsync(Guid.NewGuid(), disruptionId, seed.PatientId, seed.AppointmentId, "Cancelled", now, null);
    }

    private Task InsertResolvedItemRawAsync(Guid id, Guid disruptionId, Guid patientId, Guid appointmentId, string outcome, DateTimeOffset? resolvedAt, string? note)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        return ExecuteAsync($"INSERT INTO DisruptionWorklistItems (Id, DisruptionId, PatientId, AppointmentId, QueueEntryId, Outcome, UpdatedAtUtc, ResolvedAtUtc, ResolutionNote) VALUES ({id}, {disruptionId}, {patientId}, {appointmentId}, NULL, {outcome}, {updatedAt}, {resolvedAt}, {note})");
    }

    [Fact]
    public async Task A_final_unique_index_conflict_in_the_unit_of_work_is_reported_as_the_friendly_slot_message()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        await using var context = CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        var date = new DateOnly(2027, 4, 10);
        var start = new TimeOnly(15, 0);
        var first = Appointment.Request(seed.PatientId, seed.DoctorId, date, start, start.AddMinutes(20), DateTimeOffset.UtcNow);
        await unitOfWork.ExecuteSerializableWithRetryAsync(ct => { context.Appointments.Add(first); return Task.FromResult(true); }, CancellationToken.None);

        var duplicate = Appointment.Request(seed.PatientId, seed.DoctorId, date, start, start.AddMinutes(20), DateTimeOffset.UtcNow);
        var exception = await Assert.ThrowsAsync<UniqueConstraintConflictException>(() =>
            unitOfWork.ExecuteSerializableWithRetryAsync(ct => { context.Appointments.Add(duplicate); return Task.FromResult(true); }, CancellationToken.None));

        Assert.Equal("The requested appointment time is already reserved.", exception.Message);
        Assert.Equal("IX_Appointments_DoctorId_AppointmentDate_StartTime", exception.ConstraintName);
    }

    [Fact]
    public async Task Disruption_contact_history_and_doctor_break_links_are_enforced()
    {
        var seed = await SeedAsync(withWorkingHours: true);
        var date = new DateOnly(2027, 4, 3);
        var disruptionId = Guid.NewGuid();
        await InsertDisruptionRawAsync(disruptionId, seed.DoctorId, date, new TimeOnly(11, 0), "Emergency", seed.UserId, null, null);
        var itemId = Guid.NewGuid();
        await InsertWorklistItemRawAsync(itemId, disruptionId, seed.PatientId, seed.AppointmentId, null, "Pending", null, null);

        await AssertSqlConstraintErrorAsync(() => InsertAttemptRawAsync(itemId, seed.UserId, "Done"), "CK_DisruptionContactAttempts_Outcome");
        await InsertAttemptRawAsync(itemId, seed.UserId, "Contacted");
        await AssertSqlConstraintErrorAsync(() => InsertBreakWithDisruptionRawAsync(seed.DoctorId, date, new TimeOnly(11, 0), new TimeOnly(13, 0), Guid.NewGuid()),
            "FK_DoctorBreaks_DoctorDisruptions_DisruptionId");
        await InsertBreakWithDisruptionRawAsync(seed.DoctorId, date, new TimeOnly(11, 0), new TimeOnly(13, 0), disruptionId);
    }

    private async Task<Guid> InsertUserAsync()
    {
        var id = Guid.NewGuid();
        await ExecuteWithContextAsync(async context =>
        {
            context.Users.Add(new ApplicationUser { Id = id, UserName = "u" + id.ToString("N"), FullName = "Range Test User" });
            await context.SaveChangesAsync();
        });
        return id;
    }

    private async Task<SeededRecords> SeedAsync(bool withWorkingHours = false)
    {
        var userId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var specializationId = Guid.NewGuid();
        var specializationName = "Specialty-" + specializationId.ToString("N");
        var patientPhoneNumber = "+2010" + Guid.NewGuid().ToString("N")[..8];
        await using var context = CreateContext();
        context.Users.Add(new ApplicationUser { Id = userId, UserName = "u" + userId.ToString("N"), FullName = "Test User" });
        context.Specializations.Add(new Specialization(specializationId, specializationName));
        context.Patients.Add(new Patient(patientId, userId, "Test Patient", patientPhoneNumber));
        context.Doctors.Add(new Doctor(doctorId, userId, "Test Doctor", specializationId, 20));
        if (withWorkingHours)
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
                context.DoctorWorkingHours.Add(DoctorWorkingHour.Create(doctorId, day, new TimeOnly(9, 0), new TimeOnly(11, 0)));
        }
        var appointment = Appointment.Request(patientId, doctorId, new DateOnly(2027, 3, 8), new TimeOnly(9, 0), new TimeOnly(9, 20), DateTimeOffset.UtcNow);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return new SeededRecords(userId, patientId, doctorId, specializationId, specializationName, appointment.Id, patientPhoneNumber);
    }

    private async Task<bool> TryBookAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start)
    {
        await using var context = CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        // Pin the clock to midnight of the booking day so the 09:20 slot is never "already passed" (the default clock reads
        // the real time of day, which made this test depend on when it ran).
        var clock = new FixedClinicClock(date, new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var appointmentRepository = new EfAppointmentRepository(context);
        var service = new AppointmentService(appointmentRepository, unitOfWork, clock, new EfQueueRepository(context),
            new DoctorAvailabilityService(appointmentRepository, new EfDoctorScheduleRepository(context), clock));
        try
        {
            await service.RequestAsync(patientId, doctorId, date, start, CancellationToken.None);
            return true;
        }
        catch (DbUpdateException exception) when (GetSqlException(exception) is { Number: 2601 or 2627 })
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<Guid> RegisterWalkInAsync(Guid patientId, Guid doctorId, DateOnly date)
    {
        await using var context = CreateContext();
        var appointments = new EfAppointmentRepository(context);
        var queue = new EfQueueRepository(context);
        var disruptions = new EfDoctorDisruptionRepository(context);
        var clock = new FixedClinicClock(date);
        var unitOfWork = new EfUnitOfWork(context);
        var service = new CheckInService(appointments, queue,
            new UnusedExpectedDurationService(), disruptions, unitOfWork, clock,
            new GracePeriodService(appointments, queue, new UnusedExpectedDurationService(), disruptions, unitOfWork, clock),
            new DoctorCallWindowService(appointments, disruptions, new EfDoctorScheduleRepository(context)));
        return await service.RegisterWalkInAsync(patientId, doctorId, CancellationToken.None);
    }

    private async Task<bool> TryRequestAdmissionAsync(Guid queueEntryId)
    {
        await using var context = CreateContext();
        var service = CreateAdmissionService(context, new FixedClinicClock(new DateOnly(2027, 3, 10), new DateTimeOffset(2027, 3, 10, 10, 0, 0, TimeSpan.Zero)));
        try
        {
            await service.RequestAsync(queueEntryId, CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<bool> TryDecideAdmissionAsync(Guid approvalId, Guid doctorUserId, bool approve)
    {
        await using var context = CreateContext();
        var service = CreateAdmissionService(context, new FixedClinicClock(new DateOnly(2027, 3, 10), new DateTimeOffset(2027, 3, 10, 10, 0, 0, TimeSpan.Zero)));
        try
        {
            await service.DecideAsync(approvalId, doctorUserId, approve, "race", CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<bool> TryCallAdmissionAsync(Guid queueEntryId, Guid approvalId, DateOnly date, DateTimeOffset now)
    {
        await using var context = CreateContext();
        var service = CreateAdmissionService(context, new FixedClinicClock(date, now));
        try
        {
            return (await service.CallAsync(queueEntryId, approvalId, CancellationToken.None)).Called;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static WalkInAdmissionApprovalService CreateAdmissionService(MediCareDbContext context, IClinicClock clock) =>
        new(new EfQueueRepository(context), new EfAppointmentRepository(context), new EfWalkInAdmissionApprovalRepository(context),
            new EfUnitOfWork(context), clock, Options.Create(new WalkInAdmissionApprovalOptions()),
            new DoctorAvailabilityService(new EfAppointmentRepository(context), new EfDoctorScheduleRepository(context), clock),
            new DoctorCallWindowService(new EfAppointmentRepository(context), new EfDoctorDisruptionRepository(context), new EfDoctorScheduleRepository(context)));

    private Task InsertAppointmentAsync(Appointment appointment) => ExecuteWithContextAsync(context =>
    {
        context.Appointments.Add(appointment);
        return context.SaveChangesAsync();
    });

    private Task InsertPatientAsync(Guid patientId, Guid userId, string phoneNumber = "01000000001") => ExecuteWithContextAsync(async context =>
    {
        context.Patients.Add(new Patient(patientId, userId, "Duplicate Patient", phoneNumber));
        await context.SaveChangesAsync();
    });

    private Task InsertDoctorAsync(Guid doctorId, Guid userId, Guid specializationId) => ExecuteWithContextAsync(async context =>
    {
        context.Doctors.Add(new Doctor(doctorId, userId, "Duplicate Doctor", specializationId, 20));
        await context.SaveChangesAsync();
    });

    private async Task<Guid> InsertSpecializationAsync(string name)
    {
        var id = Guid.NewGuid();
        await ExecuteWithContextAsync(async context =>
        {
            context.Specializations.Add(new Specialization(id, name));
            await context.SaveChangesAsync();
        });
        return id;
    }

    private Task InsertDoctorRawAsync(Guid doctorId, Guid userId, Guid specializationId, int duration) =>
        ExecuteAsync($"INSERT INTO Doctors (Id, ApplicationUserId, FullName, SpecializationId, AppointmentDurationMinutes, IsActive) VALUES ({doctorId}, {userId}, N'Invalid doctor', {specializationId}, {duration}, 1)");

    private Task InsertAppointmentRawAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end) =>
        ExecuteAsync($"INSERT INTO Appointments (Id, PatientId, DoctorId, AppointmentDate, StartTime, EndTime, Status, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {patientId}, {doctorId}, {date}, {start}, {end}, N'Cancelled', {DateTimeOffset.UtcNow})");

    private Task InsertBreakRawAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end) =>
        ExecuteAsync($"INSERT INTO DoctorBreaks (Id, DoctorId, Date, StartTime, EndTime) VALUES ({Guid.NewGuid()}, {doctorId}, {date}, {start}, {end})");

    private Task InsertWorkingHourRawAsync(Guid doctorId, TimeOnly start, TimeOnly end) =>
        ExecuteAsync($"INSERT INTO DoctorWorkingHours (Id, DoctorId, DayOfWeek, StartTime, EndTime, IsActive) VALUES ({Guid.NewGuid()}, {doctorId}, 1, {start}, {end}, 1)");

    private Task InsertHolidayRawAsync(Guid doctorId, DateOnly start, DateOnly end) =>
        ExecuteAsync($"INSERT INTO DoctorHolidays (Id, DoctorId, StartDate, EndDate) VALUES ({Guid.NewGuid()}, {doctorId}, {start}, {end})");

    private Task<Guid> InsertQueueRawAsync(Guid patientId, Guid doctorId, Guid? appointmentId, DateOnly date, string source, DateTimeOffset arrivedAt, long? sequence, string status)
    {
        var id = Guid.NewGuid();
        return ExecuteQueueInsertAsync(id, patientId, doctorId, appointmentId, date, source, arrivedAt, sequence, status);
    }

    private Task InsertAdmissionRawAsync(Guid queueEntryId, Guid doctorId, string decision, DateTimeOffset requestedAt, DateTimeOffset expiresAt, DateTimeOffset? decisionAt, DateTimeOffset? usedAt) =>
        ExecuteAsync($"INSERT INTO WalkInAdmissionApprovals (Id, QueueEntryId, DoctorId, Decision, RequestedAtUtc, ExpiresAtUtc, DecisionAtUtc, UsedAtUtc) VALUES ({Guid.NewGuid()}, {queueEntryId}, {doctorId}, {decision}, {requestedAt}, {expiresAt}, {decisionAt}, {usedAt})");

    private async Task<Guid> ExecuteQueueInsertAsync(Guid id, Guid patientId, Guid doctorId, Guid? appointmentId, DateOnly date, string source, DateTimeOffset arrivedAt, long? sequence, string status)
    {
        await ExecuteAsync($"INSERT INTO QueueEntries (Id, PatientId, DoctorId, AppointmentId, QueueDate, Source, ArrivedAt, QueueSequence, Status, CreatedAt) VALUES ({id}, {patientId}, {doctorId}, {appointmentId}, {date}, {source}, {arrivedAt}, {sequence}, {status}, {arrivedAt})");
        return id;
    }

    private Task DeleteAsync(string table, Guid id)
    {
        if (table is not ("Doctors" or "Patients" or "Appointments" or "Specializations" or "AspNetUsers"))
            throw new ArgumentOutOfRangeException(nameof(table));

        return DeleteTableRowAsync(table, id);
    }

    private async Task DeleteTableRowAsync(string table, Guid id)
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DELETE FROM [{table}] WHERE [Id] = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(FormattableString command)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlAsync(command);
    }

    private async Task ExecuteWithContextAsync(Func<MediCareDbContext, Task> action)
    {
        await using var context = CreateContext();
        await action(context);
    }

    private async Task AssertSqlErrorAsync(Func<Task> action, params int[] expectedNumbers)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        var sqlException = GetSqlException(exception);
        Assert.NotNull(sqlException);
        Assert.Contains(sqlException!.Number, expectedNumbers);
    }

    private async Task AssertSqlConstraintErrorAsync(Func<Task> action, string expectedConstraint)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        var sqlException = GetSqlException(exception);
        Assert.NotNull(sqlException);
        Assert.Equal(547, sqlException!.Number);
        Assert.Contains(expectedConstraint, sqlException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SqlException? GetSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }

    private sealed class FailAfterIdentityDoctorRepository(IAccountProvisioningRepository inner) : IAccountProvisioningRepository
    {
        public Task<bool> PatientPhoneExistsAsync(string canonicalPhone, CancellationToken cancellationToken) => inner.PatientPhoneExistsAsync(canonicalPhone, cancellationToken);
        public Task<bool> SpecializationExistsAsync(Guid specializationId, CancellationToken cancellationToken) => inner.SpecializationExistsAsync(specializationId, cancellationToken);
        public Task<IReadOnlyList<SpecializationChoice>> GetSpecializationsAsync(CancellationToken cancellationToken) => inner.GetSpecializationsAsync(cancellationToken);
        public Task AddPatientAsync(Patient patient, CancellationToken cancellationToken) => inner.AddPatientAsync(patient, cancellationToken);
        public async Task AddDoctorAsync(Doctor doctor, CancellationToken cancellationToken)
        {
            await inner.AddDoctorAsync(doctor, cancellationToken);
            throw new InvalidOperationException("Simulated Doctor profile failure after Identity user persistence.");
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => inner.SaveChangesAsync(cancellationToken);
    }

    private sealed record SeededRecords(Guid UserId, Guid PatientId, Guid DoctorId, Guid SpecializationId, string SpecializationName, Guid AppointmentId, string PatientPhoneNumber);

    private sealed class UnusedExpectedDurationService : IExpectedDurationService
    {
        public Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Walk-in registration does not calculate expected duration.");
    }

    private sealed class FixedClinicClock(DateOnly today, DateTimeOffset? utcNow = null) : IClinicClock
    {
        public DateTimeOffset UtcNow => utcNow ?? DateTimeOffset.UtcNow;
        public DateOnly Today => today;
        public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
        public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
            new(DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc));
    }
}

public sealed class LocalDbDatabaseFixture : IAsyncLifetime
{
    private const string LocalDbInstance = "(localdb)\\MSSQLLocalDB";
    private readonly string _connectionString;

    public string DatabaseName { get; } = $"MediCare_ConstraintTests_{Guid.NewGuid():N}";
    public string ConnectionString => _connectionString;

    public LocalDbDatabaseFixture()
    {
        _connectionString = new SqlConnectionStringBuilder
        {
            DataSource = LocalDbInstance,
            InitialCatalog = DatabaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 20
        }.ConnectionString;
    }

    public MediCareDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MediCareDbContext>().UseSqlServer(_connectionString).Options);

    public async Task InitializeAsync()
    {
        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var master = new SqlConnection(masterConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
        await drop.ExecuteNonQueryAsync();

        await using var verify = master.CreateCommand();
        verify.CommandText = "SELECT DB_ID(@name)";
        verify.Parameters.AddWithValue("@name", DatabaseName);
        var remaining = await verify.ExecuteScalarAsync();
        Assert.True(remaining is null or DBNull, $"The isolated test database '{DatabaseName}' was not dropped.");
    }
}
