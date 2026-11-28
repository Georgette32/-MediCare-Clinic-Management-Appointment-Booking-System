using System.Globalization;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Tests;

/// <summary>
/// Proves against real SQL Server that <see cref="DoctorScheduleService.ChangeAppointmentDurationAsync"/> re-times a booked day
/// without tripping the filtered unique index on Appointments (DoctorId, AppointmentDate, StartTime) for Pending and Confirmed
/// rows, and that a refused change leaves the stored rows untouched. Every check reads back through a new DbContext.
/// Runs on the shared disposable LocalDB database of the SQL Server constraint collection, which is dropped at the end.
/// Each test seeds its own doctor and patients, so tests do not depend on each other's rows.
/// </summary>
[Collection(SqlServerConstraintCollection.Name)]
public sealed class DurationChangeSqlIntegrationTests(LocalDbDatabaseFixture database)
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly DateOnly EffectiveDay = Today.AddDays(1);
    private static readonly DateOnly NextDay = Today.AddDays(2);
    private static readonly DateTimeOffset NowUtc = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly string[] EightStarts = ["10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30"];

    private sealed record SeededDoctor(Guid DoctorId, Guid UserId);

    // ─── 1. collision: 30 -> 35 moves 13:30 onto an old start ─────────────────

    [Fact]
    public async Task Thirty_to_thirty_five_retimes_the_day_without_violating_the_live_start_index()
    {
        await using (var check = database.CreateContext())
            await AssertLiveStartIndexAsync(check);

        var doctor = await SeedDoctorAsync(30);
        var patients = await BookEightAsync(doctor.DoctorId, EffectiveDay);
        var publisher = new RecordingPatientNotificationPublisher();

        var result = await ChangeAsync(doctor, 35, EffectiveDay, publisher);

        Assert.True(result.Succeeded, result.Message);
        await using var verify = database.CreateContext();
        var day = await ReadDayAsync(verify, doctor.DoctorId, EffectiveDay);
        Assert.Equal(new[] { "10:00", "10:35", "11:10", "11:45", "12:20", "12:55", "13:30", "14:05" }, StartsOf(day));
        Assert.Equal(new[] { "10:35", "11:10", "11:45", "12:20", "12:55", "13:30", "14:05", "14:40" }, EndsOf(day));
        Assert.All(day, item => Assert.Equal(35, (item.EndTime.ToTimeSpan() - item.StartTime.ToTimeSpan()).TotalMinutes));
        Assert.Equal(patients, day.Select(item => item.PatientId).ToArray());
        Assert.Equal(AppointmentStatus.Pending, day[7].Status);
        Assert.All(day.Take(7), item => Assert.Equal(AppointmentStatus.Confirmed, item.Status));

        var extension = Assert.Single(await LoadExtensionsAsync(verify, doctor.DoctorId));
        Assert.Equal(EffectiveDay, extension.Date);
        Assert.Equal(T("14:00"), extension.OriginalEndTime);
        Assert.Equal(T("14:40"), extension.ExtendedEndTime);
        Assert.Equal(WorkingHourExtensionStatus.Approved, extension.Status);

        var doctorRow = await verify.Doctors.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId);
        Assert.Equal(35, doctorRow.DurationOn(EffectiveDay));
        Assert.Equal(30, doctorRow.DurationOn(Today));

        // The 10:00 appointment keeps its start, so seven patients are told.
        Assert.Equal(7, publisher.Published.Count);
        Assert.All(publisher.Published, notice => Assert.Equal(PatientNotificationKind.AppointmentRescheduledByClinic, notice.Kind));
    }

    // ─── 2. compression: 30 -> 25 moves later, earlier and end-only rows in one day ─────

    [Fact]
    public async Task Thirty_to_twenty_five_compresses_the_day_in_order_with_no_extension()
    {
        var doctor = await SeedDoctorAsync(30);
        var patients = await BookEightAsync(doctor.DoctorId, EffectiveDay);

        var result = await ChangeAsync(doctor, 25, EffectiveDay, new RecordingPatientNotificationPublisher());

        Assert.True(result.Succeeded, result.Message);
        await using var verify = database.CreateContext();
        var day = await ReadDayAsync(verify, doctor.DoctorId, EffectiveDay);
        Assert.Equal(new[] { "10:00", "10:25", "10:50", "11:40", "12:05", "12:30", "12:55", "13:20" }, StartsOf(day));
        Assert.All(day, item => Assert.Equal(25, (item.EndTime.ToTimeSpan() - item.StartTime.ToTimeSpan()).TotalMinutes));
        Assert.Equal(patients, day.Select(item => item.PatientId).ToArray());
        Assert.Empty(await LoadExtensionsAsync(verify, doctor.DoctorId));
    }

    // ─── 3. rollback: a refusal on the second day leaves both days as they were ─────────

    [Fact]
    public async Task Refusal_on_a_later_day_leaves_every_stored_appointment_and_the_duration_unchanged()
    {
        var doctor = await SeedDoctorAsync(30);
        var otherDoctor = await SeedDoctorAsync(30);
        var patients = await BookEightAsync(doctor.DoctorId, EffectiveDay);

        // The effective day plans cleanly. The next day has one booking for a patient who is also booked with another
        // doctor from 10:30, so re-timing it to 10:00-10:35 overlaps that booking and the whole change is refused.
        var overlappingPatient = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            var (user, patient) = NewPatient(overlappingPatient);
            seed.Users.Add(user);
            seed.Patients.Add(patient);
            seed.Appointments.Add(Booked(overlappingPatient, doctor.DoctorId, NextDay, "10:00", "10:30"));
            seed.Appointments.Add(Booked(overlappingPatient, otherDoctor.DoctorId, NextDay, "10:30", "11:00"));
            await seed.SaveChangesAsync();
        }

        var before = await SnapshotAsync(doctor.DoctorId);
        var publisher = new RecordingPatientNotificationPublisher();

        var result = await ChangeAsync(doctor, 35, EffectiveDay, publisher);

        Assert.False(result.Succeeded);
        Assert.Contains("would overlap", result.Message);
        Assert.Empty(publisher.Published);

        await using var verify = database.CreateContext();
        var after = await SnapshotAsync(doctor.DoctorId);
        Assert.Equal(before, after);
        var day = await ReadDayAsync(verify, doctor.DoctorId, EffectiveDay);
        Assert.Equal(EightStarts, StartsOf(day));
        Assert.Equal(8, day.Count);
        Assert.Equal(patients, day.Select(item => item.PatientId).ToArray());
        Assert.Empty(await LoadExtensionsAsync(verify, doctor.DoctorId));

        var doctorRow = await verify.Doctors.AsNoTracking().SingleAsync(item => item.Id == doctor.DoctorId);
        Assert.Null(doctorRow.NextAppointmentDurationMinutes);
        Assert.Equal(30, doctorRow.DurationOn(EffectiveDay));
    }

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static async Task AssertLiveStartIndexAsync(MediCareDbContext context)
    {
        var count = await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value]
            FROM sys.indexes i
            WHERE i.name = 'IX_Appointments_DoctorId_AppointmentDate_StartTime'
              AND OBJECT_NAME(i.object_id) = 'Appointments'
              AND i.is_unique = 1
              AND i.has_filter = 1
              AND i.filter_definition LIKE '%Pending%'
              AND i.filter_definition LIKE '%Confirmed%'
            """).SingleAsync();
        Assert.Equal(1, count);
    }

    private async Task<SeededDoctor> SeedDoctorAsync(int duration)
    {
        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var specialization = new Specialization(Guid.NewGuid(), "Duration-" + token);

        await using var context = database.CreateContext();
        context.Users.Add(new ApplicationUser { Id = userId, UserName = "dur-" + token, FullName = "Duration doctor" });
        context.Specializations.Add(specialization);
        context.Doctors.Add(new Doctor(doctorId, userId, "Duration doctor", specialization.Id, duration));
        foreach (var day in Enum.GetValues<DayOfWeek>())
            context.DoctorWorkingHours.Add(DoctorWorkingHour.Create(doctorId, day, T("10:00"), T("14:00")));
        await context.SaveChangesAsync();
        return new SeededDoctor(doctorId, userId);
    }

    /// <summary>Eight Confirmed 30-minute bookings from 10:00 to 13:30. The 13:30 booking is Pending. Returns the patients in start order.</summary>
    private async Task<Guid[]> BookEightAsync(Guid doctorId, DateOnly date)
    {
        await using var context = database.CreateContext();
        var patients = new List<Guid>();
        for (var i = 0; i < EightStarts.Length; i++)
        {
            var (user, patient) = NewPatient(Guid.NewGuid());
            context.Users.Add(user);
            context.Patients.Add(patient);
            patients.Add(patient.Id);

            var appointment = Appointment.Request(patient.Id, doctorId, date, T(EightStarts[i]), T(EightStarts[i]).AddMinutes(30), NowUtc);
            if (i < EightStarts.Length - 1)
                appointment.Confirm(NowUtc);
            context.Appointments.Add(appointment);
        }
        await context.SaveChangesAsync();
        return patients.ToArray();
    }

    private static Appointment Booked(Guid patientId, Guid doctorId, DateOnly date, string start, string end)
    {
        var appointment = Appointment.Request(patientId, doctorId, date, T(start), T(end), NowUtc);
        appointment.Confirm(NowUtc);
        return appointment;
    }

    private static (ApplicationUser User, Patient Patient) NewPatient(Guid patientId)
    {
        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, UserName = "pat-" + token, FullName = "Duration patient" };
        var phone = "+2010" + Random.Shared.Next(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        return (user, new Patient(patientId, userId, "Duration patient", phone));
    }

    private async Task<ManagementResult> ChangeAsync(SeededDoctor doctor, int minutes, DateOnly effectiveFrom, RecordingPatientNotificationPublisher publisher)
    {
        await using var context = database.CreateContext();
        var clock = new FixedClock(NowUtc, Today);
        var appointments = new EfAppointmentRepository(context);
        var schedule = new EfDoctorScheduleRepository(context);
        var service = new DoctorScheduleService(
            appointments,
            schedule,
            new EfDoctorScheduleManagementRepository(context),
            new DoctorAvailabilityService(appointments, schedule, clock),
            new EfDoctorDisruptionRepository(context),
            new EfUnitOfWork(context),
            clock,
            publisher);

        return await service.ChangeAppointmentDurationAsync(
            new ScheduleActor(doctor.UserId, IsAdmin: false), doctor.DoctorId, minutes, effectiveFrom, CancellationToken.None);
    }

    private async Task<string> SnapshotAsync(Guid doctorId)
    {
        await using var context = database.CreateContext();
        var rows = await context.Appointments.AsNoTracking()
            .Where(item => item.DoctorId == doctorId)
            .OrderBy(item => item.AppointmentDate).ThenBy(item => item.StartTime)
            .ToListAsync();
        return string.Join(";", rows.Select(item =>
            $"{item.AppointmentDate:yyyy-MM-dd} {item.StartTime:HH:mm}-{item.EndTime:HH:mm} {item.Status} {item.PatientId}"));
    }

    private static Task<List<Appointment>> ReadDayAsync(MediCareDbContext context, Guid doctorId, DateOnly date) =>
        context.Appointments.AsNoTracking()
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate == date)
            .OrderBy(item => item.StartTime)
            .ToListAsync();

    private static Task<List<DoctorWorkingHourExtension>> LoadExtensionsAsync(MediCareDbContext context, Guid doctorId) =>
        context.DoctorWorkingHourExtensions.AsNoTracking()
            .Where(item => item.DoctorId == doctorId)
            .ToListAsync();

    private static string[] StartsOf(IEnumerable<Appointment> items) =>
        items.Select(item => item.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();

    private static string[] EndsOf(IEnumerable<Appointment> items) =>
        items.Select(item => item.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();

    private static TimeOnly T(string hhmm) => TimeOnly.Parse(hhmm, CultureInfo.InvariantCulture);
}
