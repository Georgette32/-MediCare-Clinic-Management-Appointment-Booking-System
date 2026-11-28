using System.Globalization;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>
/// A scheduled appointment-length change re-times the booked days from its effective date on, extends a window when
/// the day no longer fits, and can be cancelled (which moves the appointments back).
/// </summary>
public sealed class DoctorDurationChangeTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required ScheduleWorld World { get; init; }
        public required DoctorScheduleService Service { get; init; }
        public required DoctorAvailabilityService Availability { get; init; }
        public required InMemoryDoctorDisruptionRepository Disruptions { get; init; }
        public required RecordingPatientNotificationPublisher Notifications { get; init; }
        public required Guid DoctorId { get; init; }
        public required Guid DoctorUserId { get; init; }

        public ScheduleActor Owner => new(DoctorUserId, IsAdmin: false);
        public Doctor Doctor => World.Doctors.Single();

        public Appointment Book(DateOnly date, string start, string end, AppointmentStatus status = AppointmentStatus.Confirmed, Guid? patientId = null) =>
            World.AddAppointment(DoctorId, date, T(start), T(end), status, patientId);

        public DoctorWorkingHour WindowOn(DateOnly date) =>
            World.WorkingHours.Single(item => item.DayOfWeek == date.DayOfWeek);

        public IReadOnlyList<Appointment> LiveOn(DateOnly date) =>
            World.Appointments.Where(item => item.DoctorId == DoctorId && item.AppointmentDate == date).OrderBy(item => item.StartTime).ToArray();
    }

    private static Harness Create(int duration, string windowStart = "10:00", string windowEnd = "14:00")
    {
        var world = new ScheduleWorld();
        var doctorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        world.Doctors.Add(new Doctor(doctorId, userId, "Dr. Duration", Guid.NewGuid(), duration));
        foreach (var day in Enum.GetValues<DayOfWeek>())
            world.WorkingHours.Add(DoctorWorkingHour.Create(doctorId, day, T(windowStart), T(windowEnd)));

        var clock = new FixedClock(Now, Today);
        var availability = new DoctorAvailabilityService(world, world, clock);
        var disruptions = new InMemoryDoctorDisruptionRepository();
        var notifications = new RecordingPatientNotificationPublisher();
        var service = new DoctorScheduleService(world, world, world, availability, disruptions, new InMemoryUnitOfWork(), clock, notifications);
        return new Harness
        {
            World = world,
            Service = service,
            Availability = availability,
            Disruptions = disruptions,
            Notifications = notifications,
            DoctorId = doctorId,
            DoctorUserId = userId
        };
    }

    private static TimeOnly T(string hhmm) => TimeOnly.Parse(hhmm, CultureInfo.InvariantCulture);

    private static DateOnly Day(int offset) => Today.AddDays(offset);

    private static string[] StartsOf(IEnumerable<Appointment> items) =>
        items.Select(item => item.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();

    private static string[] EndsOf(IEnumerable<Appointment> items) =>
        items.Select(item => item.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();

    /// <summary>Eight 30-minute bookings from 10:00 to 13:30 on <paramref name="date"/>. The last one is Pending.</summary>
    private static List<Guid> BookEight(Harness h, DateOnly date)
    {
        var patients = new List<Guid>();
        var starts = new[] { "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00", "13:30" };
        for (var i = 0; i < starts.Length; i++)
        {
            var patient = Guid.NewGuid();
            patients.Add(patient);
            var start = T(starts[i]);
            h.World.AddAppointment(h.DoctorId, date, start, start.AddMinutes(30),
                i == starts.Length - 1 ? AppointmentStatus.Pending : AppointmentStatus.Confirmed, patient);
        }
        return patients;
    }

    // ─── effective date and boundary ───────────────────────────────────────

    [Fact]
    public async Task Change_leaves_earlier_days_untouched_and_retimes_the_effective_day()
    {
        var h = Create(30);
        h.Book(Day(1), "10:00", "10:30");
        h.Book(Day(2), "10:00", "10:30");
        h.Book(Day(3), "10:00", "10:30", AppointmentStatus.Pending);
        h.Book(Day(3), "10:30", "11:00", AppointmentStatus.Pending);

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(3), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("35 min from " + Day(3).ToString("yyyy-MM-dd"), result.Message);
        Assert.Equal(new[] { "10:00" }, StartsOf(h.LiveOn(Day(1))));
        Assert.Equal(new[] { "10:30" }, EndsOf(h.LiveOn(Day(1))));
        Assert.Equal(new[] { "10:00" }, StartsOf(h.LiveOn(Day(2))));
        Assert.Equal(new[] { "10:00", "10:35" }, StartsOf(h.LiveOn(Day(3))));
        Assert.Equal(new[] { "10:35", "11:10" }, EndsOf(h.LiveOn(Day(3))));
        Assert.All(h.LiveOn(Day(3)), item => Assert.Equal(AppointmentStatus.Pending, item.Status));
        Assert.Equal(30, h.Doctor.DurationOn(Day(2)));
        Assert.Equal(35, h.Doctor.DurationOn(Day(3)));
    }

    [Fact]
    public async Task Change_effective_today_is_refused()
    {
        var h = Create(30);

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Today, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("after today", result.Message);
        Assert.Null(h.Doctor.NextAppointmentDurationMinutes);
    }

    // ─── re-timing with overflow ───────────────────────────────────────────

    [Fact]
    public async Task Thirty_to_thirty_five_keeps_order_extends_the_window_and_keeps_pending_status()
    {
        var h = Create(30);
        var patients = BookEight(h, Day(1));

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var day = h.LiveOn(Day(1));
        Assert.Equal(new[] { "10:00", "10:35", "11:10", "11:45", "12:20", "12:55", "13:30", "14:05" }, StartsOf(day));
        Assert.Equal(new[] { "10:35", "11:10", "11:45", "12:20", "12:55", "13:30", "14:05", "14:40" }, EndsOf(day));
        Assert.Equal(patients, day.Select(item => item.PatientId).ToArray());
        Assert.Equal(AppointmentStatus.Pending, day[7].Status);
        Assert.Equal(AppointmentStatus.Confirmed, day[0].Status);

        var extension = Assert.Single(h.World.Extensions);
        Assert.Equal(Day(1), extension.Date);
        Assert.Equal(T("14:00"), extension.OriginalEndTime);
        Assert.Equal(T("14:40"), extension.ExtendedEndTime);
        Assert.Equal(WorkingHourExtensionStatus.Approved, extension.Status);
    }

    [Fact]
    public async Task Each_patient_whose_start_changed_gets_one_clinic_notification_naming_the_times()
    {
        var h = Create(30);
        BookEight(h, Day(1));

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        // The 10:00 appointment keeps its start, so 7 of the 8 patients are told.
        Assert.Equal(7, h.Notifications.Published.Count);
        Assert.All(h.Notifications.Published, notice => Assert.Equal(PatientNotificationKind.AppointmentRescheduledByClinic, notice.Kind));
        Assert.All(h.Notifications.Published, notice => Assert.Contains("appointment length changed", notice.Message));
        Assert.All(h.Notifications.Published, notice => Assert.Contains(Day(1).ToString("yyyy-MM-dd"), notice.Message));
        Assert.Single(h.Notifications.Published, notice => notice.Message.Contains("from 1:30 PM to 2:05 PM"));
        Assert.Contains("7 patients notified", result.Message);
    }

    [Fact]
    public async Task Thirty_to_twenty_five_compresses_with_no_overflow_and_no_extension()
    {
        var h = Create(30);
        BookEight(h, Day(1));

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 25, Day(1), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var day = h.LiveOn(Day(1));
        Assert.Equal(new[] { "10:00", "10:25", "10:50", "11:40", "12:05", "12:30", "12:55", "13:20" }, StartsOf(day));
        Assert.All(day, item => Assert.Equal(25, (item.EndTime.ToTimeSpan() - item.StartTime.ToTimeSpan()).TotalMinutes));
        Assert.Empty(h.World.Extensions);
    }

    [Fact]
    public async Task Existing_approved_extension_is_extended_not_duplicated()
    {
        var h = Create(30);
        h.World.Extensions.Add(DoctorWorkingHourExtension.CreateApprovedByDoctor(
            h.DoctorId, Day(1), T("14:00"), T("14:10"), "Late clinic", h.DoctorUserId, Now));
        BookEight(h, Day(1));

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("14:05", StartsOf(h.LiveOn(Day(1))).Last());
        var extension = Assert.Single(h.World.Extensions);
        Assert.Equal(T("14:40"), extension.ExtendedEndTime);
        Assert.Equal(WorkingHourExtensionStatus.Approved, extension.Status);
    }

    // ─── refusals change nothing ───────────────────────────────────────────

    [Fact]
    public async Task Open_disruption_on_the_day_refuses_the_change_and_moves_nothing()
    {
        var h = Create(30, "10:00", "11:00");
        h.Book(Day(1), "10:00", "10:30");
        var disruption = DoctorDisruption.Record(h.DoctorId, Day(1), T("09:00"), "Sick leave", h.DoctorUserId, Now);
        h.Disruptions.Disruptions[disruption.Id] = disruption;
        var item = DisruptionWorklistItem.CreateForAppointment(disruption.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now);
        h.Disruptions.Items[item.Id] = item;

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("disruption", result.Message);
        Assert.Equal(new[] { "10:00" }, StartsOf(h.LiveOn(Day(1))));
        Assert.Equal(new[] { "10:30" }, EndsOf(h.LiveOn(Day(1))));
        Assert.Empty(h.World.Extensions);
    }

    [Fact]
    public async Task Patient_overlap_with_another_doctor_refuses_the_change_and_moves_nothing()
    {
        var h = Create(30, "09:00", "11:00");
        var patient = Guid.NewGuid();
        h.Book(Day(1), "09:00", "09:30", patientId: patient);
        h.World.AddAppointment(Guid.NewGuid(), Day(1), T("09:20"), T("09:50"), AppointmentStatus.Confirmed, patient);

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("would overlap", result.Message);
        Assert.Equal(new[] { "09:30" }, EndsOf(h.LiveOn(Day(1))));
        Assert.Empty(h.World.Extensions);
        Assert.Empty(h.Notifications.Published);
    }

    [Fact]
    public async Task Overflow_past_2355_refuses_the_change_and_moves_nothing()
    {
        var h = Create(30, "23:00", "23:30");
        h.Book(Day(1), "23:00", "23:30");

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 60, Day(1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("11:55 PM", result.Message);
        Assert.Equal(new[] { "23:00" }, StartsOf(h.LiveOn(Day(1))));
        Assert.Empty(h.World.Extensions);
    }

    // ─── cancelling a scheduled change ─────────────────────────────────────

    [Fact]
    public async Task Cancelling_the_scheduled_change_moves_the_appointments_back_onto_the_old_grid()
    {
        var h = Create(30);
        BookEight(h, Day(1));
        Assert.True((await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None)).Succeeded);

        var result = await h.Service.CancelScheduledDurationChangeAsync(h.Owner, h.DoctorId, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(h.Doctor.NextAppointmentDurationMinutes);
        Assert.Equal(30, h.Doctor.DurationOn(Day(1)));
        var day = h.LiveOn(Day(1));
        Assert.Equal(new[] { "10:00", "10:30", "11:00", "11:30", "12:30", "13:00", "13:30", "14:00" }, StartsOf(day));
        Assert.All(day, item => Assert.Equal(30, (item.EndTime.ToTimeSpan() - item.StartTime.ToTimeSpan()).TotalMinutes));
    }

    [Fact]
    public async Task Cancelling_when_nothing_is_scheduled_is_refused()
    {
        var h = Create(30);

        var result = await h.Service.CancelScheduledDurationChangeAsync(h.Owner, h.DoctorId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no scheduled", result.Message);
    }

    // ─── working-window edits and availability ─────────────────────────────

    [Fact]
    public async Task Working_window_edit_after_a_scheduled_change_is_not_refused_by_the_new_length()
    {
        var h = Create(30);
        h.Book(Day(1), "10:30", "11:00");
        h.Book(Day(1), "11:00", "11:30");
        Assert.True((await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 35, Day(1), CancellationToken.None)).Succeeded);
        Assert.Equal(new[] { "10:35", "11:10" }, StartsOf(h.LiveOn(Day(1))));
        var window = h.WindowOn(Day(1));

        // Shrinks the start so the fit check runs. The booked 35-minute appointments still fit the new window.
        var result = await h.Service.ChangeWorkingWindowTimesAsync(h.Owner, h.DoctorId, window.Id, T("10:35"), T("14:00"), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task Availability_uses_the_new_length_on_the_effective_day_and_the_old_length_before_it()
    {
        var h = Create(30, "10:00", "11:00");
        Assert.True((await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 20, Day(3), CancellationToken.None)).Succeeded);

        var before = await h.Availability.GetAvailableSlotsAsync(h.DoctorId, Day(2), CancellationToken.None);
        var onDay = await h.Availability.GetAvailableSlotsAsync(h.DoctorId, Day(3), CancellationToken.None);

        Assert.Equal(new[] { T("10:00"), T("10:30") }, before);
        Assert.Equal(new[] { T("10:00"), T("10:20"), T("10:40") }, onDay);
    }
}
