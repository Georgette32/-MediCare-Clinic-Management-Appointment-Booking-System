using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class DoctorScheduleServiceTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeOnly Eleven = new(11, 0);

    private sealed class Harness
    {
        public required ScheduleWorld World { get; init; }
        public required DoctorScheduleService Service { get; init; }
        public required Guid DoctorId { get; init; }
        public required Guid DoctorUserId { get; init; }

        public ScheduleActor Owner => new(DoctorUserId, IsAdmin: false);
        public ScheduleActor Admin => new(Guid.NewGuid(), IsAdmin: true);
        public Doctor Doctor => World.Doctors.Single();
        public DayOfWeek Weekday => Today.DayOfWeek;
        public DoctorWorkingHour Window => World.WorkingHours.Single(item => item.IsActive);
    }

    private static Harness Create(int durationMinutes = 20, params (TimeOnly Start, TimeOnly End)[] windows)
    {
        var world = new ScheduleWorld();
        var doctorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        world.Doctors.Add(new Doctor(doctorId, userId, "Dr. Schedule", Guid.NewGuid(), durationMinutes));
        var weekday = Today.DayOfWeek;
        foreach (var window in windows)
            world.WorkingHours.Add(DoctorWorkingHour.Create(doctorId, weekday, window.Start, window.End));

        var clock = new FixedClock(Now, Today);
        var availability = new DoctorAvailabilityService(world, world, clock);
        var service = new DoctorScheduleService(world, world, world, availability, new InMemoryUnitOfWork(), clock);
        return new Harness { World = world, Service = service, DoctorId = doctorId, DoctorUserId = userId };
    }

    private static void Book(Harness harness, DateOnly date, TimeOnly start, TimeOnly end) =>
        harness.World.Appointments.Add(new ScheduleWorld.ScheduleAppointment(harness.DoctorId, date, start, end, AppointmentStatus.Confirmed));

    // ─── working windows ───────────────────────────────────────────────────

    [Fact]
    public async Task Adding_a_window_that_overlaps_an_active_window_is_refused()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.AddWorkingWindowAsync(h.Owner, h.DoctorId, h.Weekday, new TimeOnly(10, 0), new TimeOnly(12, 0), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("overlaps", result.Message);
        Assert.Single(h.World.WorkingHours);
    }

    [Fact]
    public async Task Adding_a_window_with_end_not_after_start_is_refused()
    {
        var h = Create(20);

        var result = await h.Service.AddWorkingWindowAsync(h.Owner, h.DoctorId, h.Weekday, Eleven, Nine, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("later than its start", result.Message);
        Assert.Empty(h.World.WorkingHours);
    }

    [Fact]
    public async Task Adding_a_non_overlapping_window_succeeds()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.AddWorkingWindowAsync(h.Owner, h.DoctorId, h.Weekday, new TimeOnly(13, 0), new TimeOnly(14, 0), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, h.World.WorkingHours.Count(item => item.IsActive));
    }

    [Fact]
    public async Task Adding_a_window_that_starts_where_an_inactive_one_starts_is_refused_with_guidance()
    {
        var h = Create(20, (Nine, Eleven));
        await h.Service.SetWorkingWindowActiveAsync(h.Owner, h.DoctorId, h.Window.Id, false, CancellationToken.None);

        var result = await h.Service.AddWorkingWindowAsync(h.Owner, h.DoctorId, h.Weekday, Nine, new TimeOnly(10, 0), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Reactivate or edit", result.Message);
    }

    [Fact]
    public async Task Deactivating_a_window_is_refused_when_a_future_booking_would_be_left_outside()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today.AddDays(7), new TimeOnly(9, 0), new TimeOnly(9, 20));

        var result = await h.Service.SetWorkingWindowActiveAsync(h.Owner, h.DoctorId, h.Window.Id, false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("booked appointment", result.Message);
        Assert.Contains("Reschedule or cancel", result.Message);
    }

    [Fact]
    public async Task Deactivating_a_window_succeeds_when_no_future_booking_depends_on_it()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today.AddDays(-1), new TimeOnly(9, 0), new TimeOnly(9, 20));

        var result = await h.Service.SetWorkingWindowActiveAsync(h.Owner, h.DoctorId, h.Window.Id, false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(h.World.WorkingHours.Single().IsActive);
    }

    [Fact]
    public async Task Shrinking_a_window_is_refused_when_a_booking_would_fall_outside_it()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today, new TimeOnly(10, 40), new TimeOnly(11, 0));

        var result = await h.Service.ChangeWorkingWindowTimesAsync(h.Owner, h.DoctorId, h.Window.Id, Nine, new TimeOnly(10, 30), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("booked appointment", result.Message);
    }

    [Fact]
    public async Task Moving_a_window_is_refused_when_a_booking_loses_slot_alignment()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today, new TimeOnly(9, 20), new TimeOnly(9, 40));

        // The new start (09:10) puts the 09:20 booking 10 minutes off the 20-minute grid.
        var result = await h.Service.ChangeWorkingWindowTimesAsync(h.Owner, h.DoctorId, h.Window.Id, new TimeOnly(9, 10), Eleven, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("booked appointment", result.Message);
    }

    [Fact]
    public async Task Changing_a_window_end_is_refused_while_a_live_extension_references_the_old_end()
    {
        var h = Create(20, (Nine, Eleven));
        h.World.Extensions.Add(DoctorWorkingHourExtension.CreateApprovedByDoctor(
            h.DoctorId, Today.AddDays(3), Eleven, new TimeOnly(11, 20), "Overflow clinic", h.DoctorUserId, Now));

        var result = await h.Service.ChangeWorkingWindowTimesAsync(h.Owner, h.DoctorId, h.Window.Id, Nine, new TimeOnly(12, 0), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("extension", result.Message);
    }

    [Fact]
    public async Task Deactivating_a_window_is_refused_while_a_live_extension_references_its_end()
    {
        var h = Create(20, (Nine, Eleven));
        h.World.Extensions.Add(DoctorWorkingHourExtension.Request(
            h.DoctorId, Today.AddDays(3), Eleven, new TimeOnly(11, 20), "Pending request", h.DoctorUserId, Now));

        var result = await h.Service.SetWorkingWindowActiveAsync(h.Owner, h.DoctorId, h.Window.Id, false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("extension", result.Message);
    }

    [Fact]
    public async Task Extending_a_window_without_live_extensions_or_bookings_succeeds()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.ChangeWorkingWindowTimesAsync(h.Owner, h.DoctorId, h.Window.Id, Nine, new TimeOnly(12, 0), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new TimeOnly(12, 0), h.Window.EndTime);
    }

    // ─── appointment duration ──────────────────────────────────────────────

    [Fact]
    public async Task Duration_change_is_refused_when_a_future_booking_would_no_longer_fit()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today.AddDays(7), new TimeOnly(9, 0), new TimeOnly(9, 20));

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 30, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("appointment grid", result.Message);
    }

    [Fact]
    public async Task Duration_change_succeeds_when_no_future_booking_conflicts()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today.AddDays(-3), new TimeOnly(9, 0), new TimeOnly(9, 30)); // past bookings are ignored

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 30, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(30, h.Doctor.AppointmentDurationMinutes);
    }

    [Fact]
    public async Task Duration_must_be_positive()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.ChangeAppointmentDurationAsync(h.Owner, h.DoctorId, 0, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(20, h.Doctor.AppointmentDurationMinutes);
    }

    // ─── holidays ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Holiday_is_refused_when_booked_appointments_fall_inside_the_range()
    {
        var h = Create(20, (Nine, Eleven));
        Book(h, Today.AddDays(3), Nine, new TimeOnly(9, 20));
        Book(h, Today.AddDays(4), Nine, new TimeOnly(9, 20));

        var result = await h.Service.AddHolidayAsync(h.Owner, h.DoctorId, Today.AddDays(2), Today.AddDays(5), "Conference", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("2 booked appointment(s)", result.Message);
        Assert.Empty(h.World.Holidays);
    }

    [Fact]
    public async Task Holiday_over_free_days_is_added()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.AddHolidayAsync(h.Owner, h.DoctorId, Today.AddDays(2), Today.AddDays(3), "  Leave  ", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Leave", Assert.Single(h.World.Holidays).Reason);
    }

    [Fact]
    public async Task Holiday_cannot_start_before_today()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.AddHolidayAsync(h.Owner, h.DoctorId, Today.AddDays(-1), Today, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("today or later", result.Message);
    }

    [Fact]
    public async Task Past_holiday_cannot_be_removed()
    {
        var h = Create(20, (Nine, Eleven));
        var past = DoctorHoliday.Create(h.DoctorId, Today.AddDays(-5), Today.AddDays(-1), null);
        h.World.Holidays.Add(past);

        var result = await h.Service.RemoveHolidayAsync(h.Owner, h.DoctorId, past.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(h.World.Holidays);
    }

    // ─── dated breaks ──────────────────────────────────────────────────────

    [Fact]
    public async Task Break_over_a_booked_time_is_refused_and_names_the_booked_time()
    {
        var h = Create(20, (Nine, new TimeOnly(12, 0)));
        var sameWeekday = Today.AddDays(7);
        Book(h, sameWeekday, new TimeOnly(10, 0), new TimeOnly(10, 20));

        var result = await h.Service.AddBreakAsync(h.Owner, h.DoctorId, sameWeekday, new TimeOnly(10, 10), new TimeOnly(10, 30), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("10:00 AM to 10:20 AM", result.Message);
        Assert.Empty(h.World.Breaks);
    }

    [Fact]
    public async Task Break_must_sit_inside_a_working_window()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.AddBreakAsync(h.Owner, h.DoctorId, Today.AddDays(7), new TimeOnly(12, 0), new TimeOnly(12, 30), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("inside one of the doctor's working windows", result.Message);
    }

    [Fact]
    public async Task Overlapping_breaks_on_the_same_day_are_refused()
    {
        var h = Create(20, (Nine, Eleven));
        Assert.True((await h.Service.AddBreakAsync(h.Owner, h.DoctorId, Today.AddDays(7), new TimeOnly(10, 0), new TimeOnly(10, 30), CancellationToken.None)).Succeeded);

        var result = await h.Service.AddBreakAsync(h.Owner, h.DoctorId, Today.AddDays(7), new TimeOnly(10, 20), new TimeOnly(10, 40), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("overlaps another break", result.Message);
    }

    [Fact]
    public async Task Disruption_created_break_cannot_be_removed_from_the_schedule()
    {
        var h = Create(20, (Nine, Eleven));
        var disruptionBreak = DoctorBreak.CreateForDisruption(h.DoctorId, Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(11, 0), Guid.NewGuid());
        h.World.Breaks.Add(disruptionBreak);

        var result = await h.Service.RemoveBreakAsync(h.Owner, h.DoctorId, disruptionBreak.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("disruption", result.Message);
        Assert.Single(h.World.Breaks);
    }

    [Fact]
    public async Task Future_manual_break_can_be_removed()
    {
        var h = Create(20, (Nine, Eleven));
        var manual = DoctorBreak.Create(h.DoctorId, Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(10, 20));
        h.World.Breaks.Add(manual);

        var result = await h.Service.RemoveBreakAsync(h.Owner, h.DoctorId, manual.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(h.World.Breaks);
    }

    // ─── ownership and profile ─────────────────────────────────────────────

    [Fact]
    public async Task A_doctor_cannot_change_another_doctors_schedule()
    {
        var h = Create(20, (Nine, Eleven));
        var otherDoctor = new ScheduleActor(Guid.NewGuid(), IsAdmin: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            h.Service.AddWorkingWindowAsync(otherDoctor, h.DoctorId, h.Weekday, new TimeOnly(13, 0), new TimeOnly(14, 0), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            h.Service.GetScheduleAsync(otherDoctor, h.DoctorId, CancellationToken.None));
        Assert.Single(h.World.WorkingHours);
    }

    [Fact]
    public async Task A_doctor_cannot_use_their_own_login_against_a_different_doctor_profile()
    {
        var h = Create(20, (Nine, Eleven));
        var secondDoctorId = Guid.NewGuid();
        h.World.Doctors.Add(new Doctor(secondDoctorId, Guid.NewGuid(), "Dr. Second", Guid.NewGuid(), 15));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            h.Service.ChangeAppointmentDurationAsync(h.Owner, secondDoctorId, 30, CancellationToken.None));
    }

    [Fact]
    public async Task Admin_can_manage_any_doctor_schedule()
    {
        var h = Create(20, (Nine, Eleven));

        var result = await h.Service.AddWorkingWindowAsync(h.Admin, h.DoctorId, h.Weekday, new TimeOnly(13, 0), new TimeOnly(14, 0), CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Biography_is_trimmed_saved_and_limited()
    {
        var h = Create(20, (Nine, Eleven));

        Assert.True((await h.Service.UpdateBiographyAsync(h.Owner, h.DoctorId, "  Cardiologist  ", CancellationToken.None)).Succeeded);
        Assert.Equal("Cardiologist", h.Doctor.Biography);

        var tooLong = await h.Service.UpdateBiographyAsync(h.Owner, h.DoctorId, new string('x', Doctor.MaxBiographyLength + 1), CancellationToken.None);
        Assert.False(tooLong.Succeeded);
        Assert.Equal("Cardiologist", h.Doctor.Biography);
    }

    [Fact]
    public async Task Schedule_view_marks_disruption_breaks_as_not_removable()
    {
        var h = Create(20, (Nine, Eleven));
        h.World.Breaks.Add(DoctorBreak.CreateForDisruption(h.DoctorId, Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(11, 0), Guid.NewGuid()));
        h.World.Breaks.Add(DoctorBreak.Create(h.DoctorId, Today.AddDays(1), new TimeOnly(9, 0), new TimeOnly(9, 15)));

        var view = await h.Service.GetScheduleAsync(h.Owner, h.DoctorId, CancellationToken.None);

        Assert.Equal(2, view.Breaks.Count);
        Assert.Contains(view.Breaks, item => item.IsDisruption && !item.CanRemove);
        Assert.Contains(view.Breaks, item => !item.IsDisruption && item.CanRemove);
    }
}
