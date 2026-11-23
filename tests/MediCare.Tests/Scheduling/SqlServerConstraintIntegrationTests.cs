using MediCare.Application.Common;
using MediCare.Application.Common.Exceptions;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using MediCare.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace MediCare.Tests.Scheduling;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerConstraintCollection : ICollectionFixture<LocalDbDatabaseFixture>
{
    public const string Name = "MediCare SQL Server constraints";
}

[Collection(SqlServerConstraintCollection.Name)]
public sealed class SqlServerConstraintIntegrationTests
{
    private static readonly string[] ExpectedMigrations =
    [
        "20261109164912_InitialCreate",
        "20261121143741_SyncPendingModelChanges",
        "20261123162638_IntegrateMedicalRecords",
        "20261123171455_IntegrateScheduling"
    ];

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
        foreach (var migration in ExpectedMigrations)
        {
            Assert.Contains(migration, appliedMigrations);
        }

        for (var index = 1; index < ExpectedMigrations.Length; index++)
        {
            Assert.True(
                Array.IndexOf(appliedMigrations, ExpectedMigrations[index - 1]) <
                Array.IndexOf(appliedMigrations, ExpectedMigrations[index]));
        }

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        _output.WriteLine($"LocalDB database: {_database.DatabaseName}");
        _output.WriteLine($"SQL Server version: {version}");
        _output.WriteLine("All migrations applied cleanly; no migrations or model changes are pending.");
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
        confirmed.Confirm(DateTime.UtcNow);
        await InsertAppointmentAsync(confirmed);
        await AssertSqlErrorAsync(() => InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 20), new TimeOnly(9, 45))), 2601, 2627);

        await ExecuteAsync($"UPDATE Appointments SET Status = {(int)AppointmentStatus.Cancelled} WHERE Id = {pending.Id}");
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20)));

        var rejected = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 40), new TimeOnly(10, 0));
        rejected.Reject(DateTime.UtcNow);
        await InsertAppointmentAsync(rejected);
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 40), new TimeOnly(10, 0)));
    }

    [Fact]
    public async Task Rescheduled_from_unique_index_allows_only_one_live_replacement()
    {
        var seed = await SeedAsync();
        var original = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 5), new TimeOnly(9, 0), new TimeOnly(9, 20));
        original.Confirm(DateTime.UtcNow);
        await InsertAppointmentAsync(original);

        var first = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 6), new TimeOnly(9, 0), new TimeOnly(9, 20), original.Id);
        await InsertAppointmentAsync(first);
        await AssertSqlErrorAsync(() => InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 7), new TimeOnly(9, 0), new TimeOnly(9, 20), original.Id)), 2601, 2627);

        await ExecuteAsync($"UPDATE Appointments SET Status = {(int)AppointmentStatus.Rejected} WHERE Id = {first.Id}");
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 7), new TimeOnly(9, 0), new TimeOnly(9, 20), original.Id));

        var otherOriginal = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 8), new TimeOnly(9, 0), new TimeOnly(9, 20));
        otherOriginal.Confirm(DateTime.UtcNow);
        await InsertAppointmentAsync(otherOriginal);
        var cancelled = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 9), new TimeOnly(9, 0), new TimeOnly(9, 20), otherOriginal.Id);
        await InsertAppointmentAsync(cancelled);
        await ExecuteAsync($"UPDATE Appointments SET Status = {(int)AppointmentStatus.Cancelled} WHERE Id = {cancelled.Id}");
        await InsertAppointmentAsync(NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 1, 10), new TimeOnly(9, 0), new TimeOnly(9, 20), otherOriginal.Id));
    }

    [Fact]
    public async Task Queue_source_appointment_sequence_check_rejects_each_invalid_combination()
    {
        var seed = await SeedAsync();
        var appointment = NewAppointment(seed.PatientId, seed.DoctorId, new DateOnly(2027, 2, 1), new TimeOnly(9, 0), new TimeOnly(9, 20));
        await InsertAppointmentAsync(appointment);
        var date = new DateOnly(2027, 2, 1);
        var arrival = DateTime.UtcNow;

        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date, QueueEntrySource.WalkIn, arrival, 1, QueueEntryStatus.Waiting), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.WalkIn, arrival, null, QueueEntryStatus.Waiting), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.WalkIn, arrival, 0, QueueEntryStatus.Waiting), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.Scheduled, arrival, 1, QueueEntryStatus.Waiting), "CK_QueueEntries_SourceAppointmentSequence");
        await AssertSqlConstraintErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.Scheduled, arrival, null, QueueEntryStatus.Waiting), "CK_QueueEntries_SourceAppointmentSequence");
    }

    [Fact]
    public async Task Queue_indexes_enforce_one_active_consultation_and_unique_appointment_and_daily_sequence()
    {
        var seed = await SeedAsync();
        var secondDoctorId = (await SeedAsync()).DoctorId;
        var date = new DateOnly(2027, 2, 2);
        var arrival = DateTime.UtcNow;
        var activeOne = await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.WalkIn, arrival, 1, QueueEntryStatus.InConsultation);
        await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.WalkIn, arrival.AddMinutes(1), 2, QueueEntryStatus.InConsultation), 2601, 2627);
        await InsertQueueRawAsync(seed.PatientId, secondDoctorId, null, date, QueueEntrySource.WalkIn, arrival, 1, QueueEntryStatus.InConsultation);

        var appointment = NewAppointment(seed.PatientId, seed.DoctorId, date.AddDays(1), new TimeOnly(9, 0), new TimeOnly(9, 20));
        await InsertAppointmentAsync(appointment);
        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date.AddDays(1), QueueEntrySource.Scheduled, arrival, null, QueueEntryStatus.Waiting);
        await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, appointment.Id, date.AddDays(1), QueueEntrySource.Scheduled, arrival.AddMinutes(1), null, QueueEntryStatus.Waiting), 2601, 2627);

        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.WalkIn, arrival.AddMinutes(2), 10, QueueEntryStatus.Waiting);
        await AssertSqlErrorAsync(() => InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date, QueueEntrySource.WalkIn, arrival.AddMinutes(3), 10, QueueEntryStatus.Waiting), 2601, 2627);
        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, null, date.AddDays(1), QueueEntrySource.WalkIn, arrival.AddMinutes(4), 10, QueueEntryStatus.Waiting);
    }

    [Fact]
    public async Task Doctor_and_patient_account_links_are_unique()
    {
        var seed = await SeedAsync();
        var anotherSpecializationId = await InsertSpecializationAsync("Cardiology-" + Guid.NewGuid().ToString("N"));

        await AssertSqlErrorAsync(() => InsertDoctorAsync(Guid.NewGuid(), seed.UserId, anotherSpecializationId), 2601, 2627);
        await AssertSqlErrorAsync(async () => await InsertPatientAsync(seed.UserId), 2601, 2627);
    }

    [Fact]
    public async Task Duration_and_time_date_ranges_are_enforced_by_checks()
    {
        var seed = await SeedAsync();
        var userId = await InsertUserAsync();
        await AssertSqlConstraintErrorAsync(() => InsertDoctorRawAsync(Guid.NewGuid(), userId, seed.SpecializationId, 0), "CK_DoctorProfiles_AppointmentDuration");
        await AssertSqlConstraintErrorAsync(() => InsertAppointmentRawAsync(seed.PatientId, seed.DoctorId, new DateOnly(2027, 3, 1), new TimeOnly(9, 30), new TimeOnly(9, 30)), "CK_Appointments_TimeRange");
        await AssertSqlConstraintErrorAsync(() => InsertBreakRawAsync(seed.DoctorId, new DateOnly(2027, 3, 1), new TimeOnly(10, 0), new TimeOnly(9, 0)), "CK_DoctorBreaks_TimeRange");
        await AssertSqlConstraintErrorAsync(() => InsertWorkingHourRawAsync(seed.DoctorId, new TimeOnly(10, 0), new TimeOnly(9, 0)), "CK_DoctorWorkingHours_TimeRange");
        await AssertSqlConstraintErrorAsync(() => InsertHolidayRawAsync(seed.DoctorId, new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 1)), "CK_DoctorHolidays_DateRange");
    }

    [Fact]
    public async Task No_action_relationships_and_specialization_name_uniqueness_are_enforced()
    {
        var seed = await SeedAsync();
        await InsertQueueRawAsync(seed.PatientId, seed.DoctorId, seed.AppointmentId, new DateOnly(2027, 3, 4), QueueEntrySource.Scheduled, DateTime.UtcNow, null, QueueEntryStatus.Waiting);
        await AssertSqlErrorAsync(() => DeleteAsync("DoctorProfiles", seed.DoctorId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("PatientProfiles", seed.PatientId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("Appointments", seed.AppointmentId), 547);
        await AssertSqlErrorAsync(() => DeleteAsync("Specializations", seed.SpecializationId), 547);

        // MediCare links profiles to identity users by UserId without a restrictive
        // foreign key, so deleting the user is intentionally allowed here.
        await DeleteAsync("AspNetUsers", seed.UserId);

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
                var patient = new PatientProfile(userId);
                patientIds[i] = patient.Id;
                context.Users.Add(new ApplicationUser { Id = userId, UserName = "walkin" + userId.ToString("N"), FullName = "Concurrent Patient" });
                context.PatientProfiles.Add(patient);
            }

            await context.SaveChangesAsync();
        }

        var registeredIds = await Task.WhenAll(patientIds.Select(patientId => RegisterWalkInAsync(patientId, seed.DoctorId, queueDate)));

        await using var verify = CreateContext();
        var registered = await verify.QueueEntries.Where(entry => registeredIds.Contains(entry.Id)).ToArrayAsync();
        Assert.Equal(count, registered.Length);
        Assert.Equal(count, registered.Select(entry => entry.QueueSequence).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, count).Select(value => (long)value), registered.Select(entry => entry.QueueSequence!.Value).Order());

        var repositoryOrder = await new QueueRepository(verify).GetWalkInEntriesForDoctorAndDateAsync(seed.DoctorId, queueDate, CancellationToken.None);
        var expectedOrder = registered.OrderBy(entry => entry.ArrivedAt).ThenBy(entry => entry.QueueSequence).Select(entry => entry.Id);
        Assert.Equal(expectedOrder, repositoryOrder.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Concurrent_admission_requests_for_one_entry_create_one_pending_request()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 10);
        var arrivedAt = new DateTime(2027, 3, 10, 9, 0, 0, DateTimeKind.Utc);
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
        var now = new DateTime(2027, 3, 10, 10, 0, 0, DateTimeKind.Utc);
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
        var now = new DateTime(2027, 3, 10, 10, 0, 0, DateTimeKind.Utc);
        var secondUserId = await InsertUserAsync();
        var secondPatientId = await InsertPatientAsync(secondUserId);
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
        Assert.Equal(1, await verify.QueueEntries.CountAsync(item => item.DoctorProfileId == seed.DoctorId && item.QueueDate == date && item.Status == QueueEntryStatus.Called));
        Assert.Equal(1, await verify.WalkInAdmissionApprovals.CountAsync(item =>
            (item.Id == firstApproval.Id || item.Id == secondApproval.Id) && item.UsedAtUtc != null));
    }

    [Fact]
    public async Task Walk_in_admission_decision_check_rejects_invalid_values_and_timestamp_combinations()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 11);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, DateTime.UtcNow, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var requested = DateTime.UtcNow.AddMinutes(-10);
        var expires = DateTime.UtcNow.AddMinutes(20);
        var decided = DateTime.UtcNow.AddMinutes(-5);
        var used = DateTime.UtcNow.AddMinutes(-2);

        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, 9, requested, expires, decided, null), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Pending, requested, expires, null, used), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Rejected, requested, expires, decided, used), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Expired, requested, expires, decided, used), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Approved, requested, expires, null, null), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
        await AssertSqlConstraintErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Approved, requested, expires, decided, decided.AddTicks(-1)), "CK_WalkInAdmissionApprovals_DecisionTimestamp");
    }

    [Fact]
    public async Task Walk_in_admission_decision_check_allows_valid_decisions_and_optional_approval_use()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 12);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, DateTime.UtcNow, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var requested = DateTime.UtcNow.AddMinutes(-10);
        var expires = DateTime.UtcNow.AddMinutes(20);
        var decided = DateTime.UtcNow.AddMinutes(-5);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Pending, requested, expires, null, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Approved, requested, expires, decided, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Approved, requested, expires, decided, decided);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Rejected, requested, expires, decided, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Expired, requested, expires, decided, null);
    }

    [Fact]
    public async Task Walk_in_admission_pending_index_allows_rejected_history_but_rejects_second_pending()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 13);
        var entry = QueueEntry.CreateWalkIn(seed.PatientId, seed.DoctorId, date, DateTime.UtcNow, 1);
        await using (var context = CreateContext())
        {
            context.QueueEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var requested = DateTime.UtcNow.AddMinutes(-10);
        var expires = DateTime.UtcNow.AddMinutes(20);
        var decided = DateTime.UtcNow.AddMinutes(-5);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Rejected, requested, expires, decided, null);
        await InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Pending, requested, expires, null, null);
        await AssertSqlErrorAsync(() => InsertAdmissionRawAsync(entry.Id, seed.DoctorId, (int)WalkInAdmissionDecision.Pending, requested, expires, null, null), 2601, 2627);
    }

    [Fact]
    public async Task Queue_repository_orders_scheduled_by_appointment_time_and_walk_ins_by_arrival_then_sequence()
    {
        var seed = await SeedAsync();
        var date = new DateOnly(2027, 3, 10);
        var appointmentAtTen = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(10, 0), new TimeOnly(10, 20));
        var appointmentAtNine = NewAppointment(seed.PatientId, seed.DoctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 20));
        appointmentAtTen.Confirm(DateTime.UtcNow);
        appointmentAtNine.Confirm(DateTime.UtcNow);
        var baseArrival = new DateTime(2027, 3, 10, 6, 0, 0, DateTimeKind.Utc);
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
        var repository = new QueueRepository(queryContext);
        var scheduled = await repository.GetScheduledEntriesForDoctorAndDateAsync(seed.DoctorId, date, CancellationToken.None);
        var walkIns = await repository.GetWalkInEntriesForDoctorAndDateAsync(seed.DoctorId, date, CancellationToken.None);

        Assert.Equal(new[] { appointmentAtNine.Id, appointmentAtTen.Id }, scheduled.Select(entry => entry.AppointmentId!.Value));
        Assert.Equal(new[] { earlyArrivalHigherSeq.Id, laterArrivalLowerSeq.Id, laterArrivalHigherSeq.Id }, walkIns.Select(entry => entry.Id));
    }

    private ApplicationDbContext CreateContext() => _database.CreateContext();

    private static Appointment NewAppointment(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? originalId = null) =>
        Appointment.Request(patientId, doctorId, date, start, end, DateTime.UtcNow, originalId);

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
        var specializationName = "Specialty-" + Guid.NewGuid().ToString("N");
        await using var context = CreateContext();
        context.Users.Add(new ApplicationUser { Id = userId, UserName = "u" + userId.ToString("N"), FullName = "Test User" });
        var specialization = new Specialization(specializationName);
        context.Specializations.Add(specialization);
        var patient = new PatientProfile(userId);
        context.PatientProfiles.Add(patient);
        var doctor = new DoctorProfile(
            userId,
            specialization.Id,
            "LIC-" + Guid.NewGuid().ToString("N"),
            100m,
            5,
            null,
            20);
        doctor.Approve(userId);
        context.DoctorProfiles.Add(doctor);
        if (withWorkingHours)
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
                context.DoctorWorkingHours.Add(DoctorWorkingHour.Create(doctor.Id, day, new TimeOnly(9, 0), new TimeOnly(11, 0)));
        }

        var appointment = Appointment.Request(patient.Id, doctor.Id, new DateOnly(2027, 3, 8), new TimeOnly(9, 0), new TimeOnly(9, 20), DateTime.UtcNow);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return new SeededRecords(userId, patient.Id, doctor.Id, specialization.Id, specializationName, appointment.Id);
    }

    private async Task<bool> TryBookAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start)
    {
        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var service = new AppointmentService(new AppointmentRepository(context), unitOfWork, new FixedClinicClock(date));
        try
        {
            await service.RequestAsync(patientId, doctorId, date, start, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (IsExpectedRaceFailure(exception))
        {
            return false;
        }
    }

    private async Task<Guid> RegisterWalkInAsync(Guid patientId, Guid doctorId, DateOnly date)
    {
        await using var context = CreateContext();
        var service = new CheckInService(new AppointmentRepository(context), new QueueRepository(context),
            new UnusedExpectedDurationService(), new UnitOfWork(context), new FixedClinicClock(date));
        return await service.RegisterWalkInAsync(patientId, doctorId, CancellationToken.None);
    }

    private async Task<bool> TryRequestAdmissionAsync(Guid queueEntryId)
    {
        await using var context = CreateContext();
        var service = CreateAdmissionService(context, new FixedClinicClock(new DateOnly(2027, 3, 10), new DateTime(2027, 3, 10, 10, 0, 0, DateTimeKind.Utc)));
        try
        {
            await service.RequestAsync(queueEntryId, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (IsExpectedRaceFailure(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryDecideAdmissionAsync(Guid approvalId, Guid doctorUserId, bool approve)
    {
        await using var context = CreateContext();
        var service = CreateAdmissionService(context, new FixedClinicClock(new DateOnly(2027, 3, 10), new DateTime(2027, 3, 10, 10, 0, 0, DateTimeKind.Utc)));
        try
        {
            await service.DecideAsync(approvalId, doctorUserId, approve, "race", CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (IsExpectedRaceFailure(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryCallAdmissionAsync(Guid queueEntryId, Guid approvalId, DateOnly date, DateTime now)
    {
        await using var context = CreateContext();
        var service = CreateAdmissionService(context, new FixedClinicClock(date, now));
        try
        {
            return (await service.CallAsync(queueEntryId, approvalId, CancellationToken.None)).Called;
        }
        catch (Exception exception) when (IsExpectedRaceFailure(exception))
        {
            return false;
        }
    }

    private static WalkInAdmissionApprovalService CreateAdmissionService(ApplicationDbContext context, IClinicClock clock) =>
        new(new QueueRepository(context), new AppointmentRepository(context), new WalkInAdmissionApprovalRepository(context),
            new UnitOfWork(context), clock, Options.Create(new WalkInAdmissionApprovalOptions()));

    private Task InsertAppointmentAsync(Appointment appointment) => ExecuteWithContextAsync(async context =>
    {
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
    });

    private async Task<Guid> InsertPatientAsync(Guid userId)
    {
        var patient = new PatientProfile(userId);
        await ExecuteWithContextAsync(async context =>
        {
            context.PatientProfiles.Add(patient);
            await context.SaveChangesAsync();
        });
        return patient.Id;
    }

    private Task InsertDoctorAsync(Guid doctorId, Guid userId, Guid specializationId) => ExecuteWithContextAsync(async context =>
    {
        context.DoctorProfiles.Add(new DoctorProfile(userId, specializationId, "LIC-" + doctorId.ToString("N"), 100m, 5, null, 20));
        await context.SaveChangesAsync();
    });

    private async Task<Guid> InsertSpecializationAsync(string name)
    {
        var specialization = new Specialization(name);
        await ExecuteWithContextAsync(async context =>
        {
            context.Specializations.Add(specialization);
            await context.SaveChangesAsync();
        });
        return specialization.Id;
    }

    private Task InsertDoctorRawAsync(Guid doctorId, Guid userId, Guid specializationId, int duration)
    {
        var licenseNumber = "LIC-" + doctorId.ToString("N");
        return ExecuteAsync($"INSERT INTO DoctorProfiles (Id, UserId, SpecializationId, LicenseNumber, ConsultationFee, YearsOfExperience, ApprovalStatus, AppointmentDurationMinutes, CreatedAtUtc) VALUES ({doctorId}, {userId}, {specializationId}, {licenseNumber}, {100m}, {5}, {(int)DoctorApprovalStatus.Pending}, {duration}, {DateTime.UtcNow})");
    }

    private Task InsertAppointmentRawAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var scheduledAtUtc = date.ToDateTime(start, DateTimeKind.Utc);
        return ExecuteAsync($"INSERT INTO Appointments (Id, PatientProfileId, DoctorProfileId, ScheduledAtUtc, AppointmentDate, StartTime, EndTime, Status, Fee, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {patientId}, {doctorId}, {scheduledAtUtc}, {date}, {start}, {end}, {(int)AppointmentStatus.Cancelled}, {0m}, {DateTime.UtcNow})");
    }

    private Task InsertBreakRawAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end) =>
        ExecuteAsync($"INSERT INTO DoctorBreaks (Id, DoctorProfileId, Date, StartTime, EndTime, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {doctorId}, {date}, {start}, {end}, {DateTime.UtcNow})");

    private Task InsertWorkingHourRawAsync(Guid doctorId, TimeOnly start, TimeOnly end) =>
        ExecuteAsync($"INSERT INTO DoctorWorkingHours (Id, DoctorProfileId, DayOfWeek, StartTime, EndTime, IsActive, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {doctorId}, {(int)DayOfWeek.Monday}, {start}, {end}, {1}, {DateTime.UtcNow})");

    private Task InsertHolidayRawAsync(Guid doctorId, DateOnly start, DateOnly end) =>
        ExecuteAsync($"INSERT INTO DoctorHolidays (Id, DoctorProfileId, StartDate, EndDate, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {doctorId}, {start}, {end}, {DateTime.UtcNow})");

    private Task<Guid> InsertQueueRawAsync(Guid patientId, Guid doctorId, Guid? appointmentId, DateOnly date, QueueEntrySource source, DateTime arrivedAt, long? sequence, QueueEntryStatus status)
    {
        var id = Guid.NewGuid();
        return ExecuteQueueInsertAsync(id, patientId, doctorId, appointmentId, date, source, arrivedAt, sequence, status);
    }

    private Task InsertAdmissionRawAsync(Guid queueEntryId, Guid doctorId, int decision, DateTime requestedAt, DateTime expiresAt, DateTime? decisionAt, DateTime? usedAt) =>
        ExecuteAsync($"INSERT INTO WalkInAdmissionApprovals (Id, QueueEntryId, DoctorProfileId, Decision, RequestedAtUtc, ExpiresAtUtc, DecisionAtUtc, UsedAtUtc, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {queueEntryId}, {doctorId}, {decision}, {requestedAt}, {expiresAt}, {decisionAt}, {usedAt}, {requestedAt})");

    private async Task<Guid> ExecuteQueueInsertAsync(Guid id, Guid patientId, Guid doctorId, Guid? appointmentId, DateOnly date, QueueEntrySource source, DateTime arrivedAt, long? sequence, QueueEntryStatus status)
    {
        await ExecuteAsync($"INSERT INTO QueueEntries (Id, PatientProfileId, DoctorProfileId, AppointmentId, QueueDate, Source, ArrivedAt, QueueSequence, Status, CreatedAtUtc) VALUES ({id}, {patientId}, {doctorId}, {appointmentId}, {date}, {(int)source}, {arrivedAt}, {sequence}, {(int)status}, {arrivedAt})");
        return id;
    }

    private Task DeleteAsync(string table, Guid id)
    {
        if (table is not ("DoctorProfiles" or "PatientProfiles" or "Appointments" or "Specializations" or "AspNetUsers"))
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

    private async Task ExecuteWithContextAsync(Func<ApplicationDbContext, Task> action)
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

    private static bool IsExpectedRaceFailure(Exception exception) =>
        exception is InvalidOperationException or ConcurrencyConflictException ||
        GetSqlException(exception) is { Number: 2601 or 2627 };

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

    private sealed class UnusedExpectedDurationService : IExpectedDurationService
    {
        public Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorProfileId, DateOnly clinicDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Walk-in registration does not calculate expected duration.");
    }

    private sealed class FixedClinicClock(DateOnly today, DateTime? utcNow = null) : IClinicClock
    {
        public DateTime UtcNow => utcNow ?? DateTime.UtcNow;
        public DateOnly Today => today;
        public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow);
        public DateTime ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
            DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc);
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

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_connectionString).Options);

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
