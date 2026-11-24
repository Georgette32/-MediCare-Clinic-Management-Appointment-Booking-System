using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MediCare.Infrastructure.Persistence;

namespace MediCare.Tests;

/// <summary>SQL Server checks for the schedule and clinic-settings schema. Runs on the disposable LocalDB fixture only.</summary>
[Collection(SqlServerConstraintCollection.Name)]
public sealed class ScheduleSchemaSqlTests(LocalDbDatabaseFixture database)
{
    [Fact]
    public async Task Clinic_settings_accepts_only_the_singleton_row_id()
    {
        await using var context = database.CreateContext();
        await AssertConstraintAsync(context, "INSERT INTO [ClinicSettings] ([Id], [MinimumConsultationSampleSize], [UpdatedAtUtc]) VALUES (2, NULL, SYSDATETIMEOFFSET())",
            "CK_ClinicSettings_SingletonId");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Clinic_settings_rejects_non_positive_sample_sizes(string value)
    {
        await using var context = database.CreateContext();
        await AssertConstraintAsync(context,
            $"INSERT INTO [ClinicSettings] ([Id], [MinimumConsultationSampleSize], [UpdatedAtUtc]) VALUES (1, {value}, SYSDATETIMEOFFSET())",
            "CK_ClinicSettings_MinimumSampleSizePositive");
    }

    [Fact]
    public async Task Clinic_settings_accepts_unset_and_positive_values_on_the_singleton_row()
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [ClinicSettings] WHERE [Id] = 1");
        try
        {
            await context.Database.ExecuteSqlRawAsync("INSERT INTO [ClinicSettings] ([Id], [MinimumConsultationSampleSize], [UpdatedAtUtc]) VALUES (1, NULL, SYSDATETIMEOFFSET())");
            await context.Database.ExecuteSqlRawAsync("UPDATE [ClinicSettings] SET [MinimumConsultationSampleSize] = 4 WHERE [Id] = 1");
            var value = await context.Database.SqlQueryRaw<int>("SELECT [MinimumConsultationSampleSize] AS [Value] FROM [ClinicSettings] WHERE [Id] = 1").SingleAsync();
            Assert.Equal(4, value);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [ClinicSettings] WHERE [Id] = 1");
        }
    }

    [Fact]
    public async Task Working_window_trigger_refuses_overlapping_active_windows_for_the_same_doctor_and_weekday()
    {
        await using var context = database.CreateContext();
        var doctorId = await SeedDoctorAsync(context);
        var monday = (int)DayOfWeek.Monday;

        await InsertWindowAsync(context, doctorId, monday, new TimeOnly(9, 0), new TimeOnly(11, 0), isActive: true);
        var laterWindowId = Guid.NewGuid();
        await InsertWindowAsync(context, doctorId, monday, new TimeOnly(12, 0), new TimeOnly(13, 0), isActive: true, windowId: laterWindowId);

        await AssertTriggerAsync(context, () => InsertWindowAsync(context, doctorId, monday, new TimeOnly(10, 0), new TimeOnly(12, 0), isActive: true));
        // Moving the 12:00-13:00 window onto 10:00-12:00 would overlap the 09:00-11:00 window.
        await AssertTriggerAsync(context, () => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [DoctorWorkingHours] SET [StartTime] = {new TimeOnly(10, 0)}, [EndTime] = {new TimeOnly(12, 0)} WHERE [Id] = {laterWindowId}"));
    }

    [Fact]
    public async Task Working_window_trigger_allows_adjacent_inactive_and_other_day_windows()
    {
        await using var context = database.CreateContext();
        var doctorId = await SeedDoctorAsync(context);
        var tuesday = (int)DayOfWeek.Tuesday;

        await InsertWindowAsync(context, doctorId, tuesday, new TimeOnly(9, 0), new TimeOnly(11, 0), isActive: true);
        await InsertWindowAsync(context, doctorId, tuesday, new TimeOnly(11, 0), new TimeOnly(12, 0), isActive: true);
        await InsertWindowAsync(context, doctorId, tuesday, new TimeOnly(10, 0), new TimeOnly(10, 30), isActive: false);
        await InsertWindowAsync(context, doctorId, (int)DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(11, 0), isActive: true);

        var count = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [DoctorWorkingHours] WHERE [DoctorId] = {0}", doctorId).SingleAsync();
        Assert.Equal(4, count);
    }

    private static async Task<Guid> SeedDoctorAsync(MediCareDbContext context)
    {
        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var specializationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var specializationName = "Sched-" + token;
        var userName = "sched-" + token;
        var normalizedUserName = userName.ToUpperInvariant();
        var createdAt = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [Specializations] ([Id], [Name]) VALUES ({specializationId}, {specializationName})");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [AspNetUsers]
                ([Id], [FullName], [IsActive], [CreatedAtUtc], [UserName], [NormalizedUserName], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
            VALUES
                ({userId}, N'Schedule doctor', 1, {createdAt}, {userName}, {normalizedUserName}, 0, 0, 0, 0, 0)
            """);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [Doctors] ([Id], [ApplicationUserId], [FullName], [SpecializationId], [AppointmentDurationMinutes], [IsActive]) VALUES ({doctorId}, {userId}, N'Schedule doctor', {specializationId}, 20, 1)");
        return doctorId;
    }

    private static Task InsertWindowAsync(MediCareDbContext context, Guid doctorId, int dayOfWeek, TimeOnly start, TimeOnly end, bool isActive, Guid? windowId = null)
    {
        var id = windowId ?? Guid.NewGuid();
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [DoctorWorkingHours] ([Id], [DoctorId], [DayOfWeek], [StartTime], [EndTime], [IsActive]) VALUES ({id}, {doctorId}, {dayOfWeek}, {start}, {end}, {isActive})");
    }

    private static async Task AssertTriggerAsync(MediCareDbContext context, Func<Task> action)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        Assert.Contains("Overlapping active working-hour windows", FullMessage(exception), StringComparison.Ordinal);
    }

    private static async Task AssertConstraintAsync(MediCareDbContext context, string sql, string constraintName)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(constraintName, FullMessage(exception), StringComparison.Ordinal);
    }

    private static string FullMessage(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages);
    }
}
