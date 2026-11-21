using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        _output.WriteLine($"LocalDB database: {_database.DatabaseName}");
        _output.WriteLine($"SQL Server version: {version}");
        _output.WriteLine("InitialCreate migration applied cleanly (including Status IN (...) filtered indexes).");
    }

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

    private MediCareDbContext CreateContext() => _database.CreateContext();

    private static Appointment NewAppointment(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? originalId = null) =>
        Appointment.Request(patientId, doctorId, date, start, end, DateTimeOffset.UtcNow, originalId);

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
        await using var context = CreateContext();
        context.Users.Add(new ApplicationUser { Id = userId, UserName = "u" + userId.ToString("N"), FullName = "Test User" });
        context.Specializations.Add(new Specialization(specializationId, specializationName));
        context.Patients.Add(new Patient(patientId, userId, "Test Patient", "01000000000"));
        context.Doctors.Add(new Doctor(doctorId, userId, "Test Doctor", specializationId, 20));
        if (withWorkingHours)
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
                context.DoctorWorkingHours.Add(DoctorWorkingHour.Create(doctorId, day, new TimeOnly(9, 0), new TimeOnly(11, 0)));
        }
        var appointment = Appointment.Request(patientId, doctorId, new DateOnly(2027, 3, 8), new TimeOnly(9, 0), new TimeOnly(9, 20), DateTimeOffset.UtcNow);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return new SeededRecords(userId, patientId, doctorId, specializationId, specializationName, appointment.Id);
    }

    private async Task<bool> TryBookAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start)
    {
        await using var context = CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        var service = new AppointmentService(new EfAppointmentRepository(context), unitOfWork, new FixedClinicClock(date));
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

    private Task InsertAppointmentAsync(Appointment appointment) => ExecuteWithContextAsync(context =>
    {
        context.Appointments.Add(appointment);
        return context.SaveChangesAsync();
    });

    private Task InsertPatientAsync(Guid patientId, Guid userId) => ExecuteWithContextAsync(async context =>
    {
        context.Patients.Add(new Patient(patientId, userId, "Duplicate Patient", "01000000001"));
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

    private sealed record SeededRecords(Guid UserId, Guid PatientId, Guid DoctorId, Guid SpecializationId, string SpecializationName, Guid AppointmentId);

    private sealed class FixedClinicClock(DateOnly today) : IClinicClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => today;
    }
}

public sealed class LocalDbDatabaseFixture : IAsyncLifetime
{
    private const string LocalDbInstance = "(localdb)\\MSSQLLocalDB";
    private readonly string _connectionString;

    public string DatabaseName { get; } = $"MediCare_ConstraintTests_{Guid.NewGuid():N}";

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
