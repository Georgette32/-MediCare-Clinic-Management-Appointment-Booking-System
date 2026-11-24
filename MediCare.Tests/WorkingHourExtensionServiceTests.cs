using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class WorkingHourExtensionServiceTests
{
    private static readonly DateOnly Date = new(2026, 12, 2);
    private static readonly TimeOnly WindowEnd = new(11, 0);
    private static readonly TimeOnly Extended = new(11, 40);

    [Fact]
    public async Task Reception_request_is_pending_and_offers_no_availability_until_the_doctor_approves()
    {
        var s = Fixture.Create();
        var availability = s.Availability;

        var request = await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, Extended, "Overflow clinic", s.ReceptionUserId, CancellationToken.None);

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(WorkingHourExtensionStatus.Pending, s.Schedule.Extensions[request.ExtensionId!.Value].Status);
        Assert.DoesNotContain(new TimeOnly(11, 20), await availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None));

        var decision = await s.Service.DecideAsync(request.ExtensionId!.Value, s.DoctorUserId, approve: true, notes: "Fine", CancellationToken.None);

        Assert.True(decision.Succeeded, decision.Message);
        Assert.Contains(new TimeOnly(11, 20), await availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None));
    }

    [Fact]
    public async Task Doctor_who_does_not_own_the_schedule_cannot_decide_a_request()
    {
        var s = Fixture.Create();
        var request = await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, Extended, "Overflow", s.ReceptionUserId, CancellationToken.None);

        var result = await s.Service.DecideAsync(request.ExtensionId!.Value, s.OtherDoctorUserId, approve: true, notes: null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("own schedule", result.Message);
        Assert.Equal(WorkingHourExtensionStatus.Pending, s.Schedule.Extensions[request.ExtensionId!.Value].Status);
    }

    [Fact]
    public async Task Past_dated_pending_request_cannot_be_approved_or_rejected()
    {
        var s = Fixture.Create(today: Date.AddDays(1));
        var pending = ScheduleTestData.PendingExtension(s.DoctorId, Date, WindowEnd, Extended, s.Clock.UtcNow.AddDays(-2));
        s.Schedule.Extensions[pending.Id] = pending;

        var approve = await s.Service.DecideAsync(pending.Id, s.DoctorUserId, approve: true, notes: null, CancellationToken.None);
        var reject = await s.Service.DecideAsync(pending.Id, s.DoctorUserId, approve: false, notes: null, CancellationToken.None);

        Assert.False(approve.Succeeded);
        Assert.Contains("expired", approve.Message);
        Assert.False(reject.Succeeded);
        Assert.Equal(WorkingHourExtensionStatus.Pending, pending.Status);
        Assert.Empty(await s.Service.GetPendingForDoctorUserAsync(s.DoctorUserId, CancellationToken.None));
        var shown = Assert.Single(await s.Service.GetOwnAsync(s.DoctorUserId, Date.AddDays(-1), CancellationToken.None));
        Assert.Equal("Expired", shown.DisplayStatus);
    }

    [Fact]
    public async Task Doctor_created_extension_is_approved_at_once_and_offered_immediately()
    {
        var s = Fixture.Create();

        var result = await s.Service.CreateOwnAsync(s.DoctorUserId, Date, WindowEnd, Extended, "Own extension", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(WorkingHourExtensionStatus.Approved, s.Schedule.Extensions[result.ExtensionId!.Value].Status);
        Assert.Contains(new TimeOnly(11, 20), await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None));
    }

    [Fact]
    public async Task Second_live_extension_for_the_same_window_is_refused()
    {
        var s = Fixture.Create();
        Assert.True((await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, Extended, "First", s.ReceptionUserId, CancellationToken.None)).Succeeded);

        var second = await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, new TimeOnly(11, 20), "Second", s.ReceptionUserId, CancellationToken.None);

        Assert.False(second.Succeeded);
        Assert.Equal("A live extension already exists for this window.", second.Message);
    }

    [Theory]
    [InlineData("10:30", "10:45", "does not match an active working window")]
    [InlineData("11:00", "11:40", "overlap another working window")]
    public async Task Invalid_window_or_overlapping_extension_is_refused(string originalEnd, string extendedEnd, string expectedFragment)
    {
        var s = Fixture.Create(secondWindowStart: new TimeOnly(11, 30));

        var result = await s.Service.RequestAsync(s.DoctorId, Date, TimeOnly.Parse(originalEnd), TimeOnly.Parse(extendedEnd), "Overflow", s.ReceptionUserId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(expectedFragment, result.Message);
    }

    [Fact]
    public async Task Holiday_and_past_dates_are_refused()
    {
        var holiday = Fixture.Create();
        holiday.Appointments.Holidays.Add(DoctorHoliday.Create(holiday.DoctorId, Date, Date, "Holiday"));
        var onHoliday = await holiday.Service.RequestAsync(holiday.DoctorId, Date, WindowEnd, Extended, "Overflow", holiday.ReceptionUserId, CancellationToken.None);
        Assert.False(onHoliday.Succeeded);
        Assert.Contains("holiday", onHoliday.Message);

        var past = Fixture.Create(today: Date.AddDays(1));
        var inPast = await past.Service.CreateOwnAsync(past.DoctorUserId, Date, WindowEnd, Extended, "Overflow", CancellationToken.None);
        Assert.False(inPast.Succeeded);
        Assert.Contains("today or a later date", inPast.Message);
    }

    [Fact]
    public async Task Approved_extension_with_a_booking_inside_it_cannot_be_cancelled_until_that_booking_is_gone()
    {
        var s = Fixture.Create();
        var created = await s.Service.CreateOwnAsync(s.DoctorUserId, Date, WindowEnd, Extended, "Own extension", CancellationToken.None);
        s.Schedule.Booked.Add((s.DoctorId, Date, new TimeOnly(11, 20), new TimeOnly(11, 40)));

        var refused = await s.Service.CancelAsync(created.ExtensionId!.Value, s.DoctorUserId, isDoctorOwner: true, CancellationToken.None);

        Assert.False(refused.Succeeded);
        Assert.Contains("already booked inside the extended hours", refused.Message);
        Assert.Equal(WorkingHourExtensionStatus.Approved, s.Schedule.Extensions[created.ExtensionId!.Value].Status);

        s.Schedule.Booked.Clear();
        var cancelled = await s.Service.CancelAsync(created.ExtensionId!.Value, s.DoctorUserId, isDoctorOwner: true, CancellationToken.None);

        Assert.True(cancelled.Succeeded, cancelled.Message);
        Assert.Equal(WorkingHourExtensionStatus.Cancelled, s.Schedule.Extensions[created.ExtensionId!.Value].Status);
    }

    [Fact]
    public async Task Reception_may_cancel_only_pending_requests()
    {
        var s = Fixture.Create();
        // The approved extension sits on another date with the same weekday window, so the pending request for Date is not a duplicate.
        var approved = await s.Service.CreateOwnAsync(s.DoctorUserId, Date.AddDays(7), WindowEnd, Extended, "Own", CancellationToken.None);
        var refused = await s.Service.CancelAsync(approved.ExtensionId!.Value, s.ReceptionUserId, isDoctorOwner: false, CancellationToken.None);
        Assert.False(refused.Succeeded);
        Assert.Contains("only pending", refused.Message);

        var pending = await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, new TimeOnly(11, 20), "Pending", s.ReceptionUserId, CancellationToken.None);
        var cancelled = await s.Service.CancelAsync(pending.ExtensionId!.Value, s.ReceptionUserId, isDoctorOwner: false, CancellationToken.None);
        Assert.True(cancelled.Succeeded, cancelled.Message);
    }

    [Fact]
    public async Task Doctor_cannot_cancel_another_doctors_extension()
    {
        var s = Fixture.Create();
        var request = await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, Extended, "Overflow", s.ReceptionUserId, CancellationToken.None);

        var result = await s.Service.CancelAsync(request.ExtensionId!.Value, s.OtherDoctorUserId, isDoctorOwner: true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkingHourExtensionStatus.Pending, s.Schedule.Extensions[request.ExtensionId!.Value].Status);
    }

    [Fact]
    public async Task Pending_list_shows_only_actionable_requests_for_the_linked_doctor()
    {
        var s = Fixture.Create();
        await s.Service.RequestAsync(s.DoctorId, Date, WindowEnd, Extended, "Overflow", s.ReceptionUserId, CancellationToken.None);

        var pending = await s.Service.GetPendingForDoctorUserAsync(s.DoctorUserId, CancellationToken.None);

        var item = Assert.Single(pending);
        Assert.Equal("Pending", item.DisplayStatus);
        Assert.Equal(WindowEnd, item.OriginalEndTime);
        Assert.Equal(Extended, item.ExtendedEndTime);
    }

    private sealed class Fixture
    {
        private Fixture(WorkingHourExtensionService service, DoctorAvailabilityService availability, InMemoryAppointmentRepository appointments,
            InMemoryDoctorScheduleRepository schedule, FixedClock clock, Guid doctorId, Guid doctorUserId, Guid otherDoctorUserId, Guid receptionUserId)
        {
            Service = service;
            Availability = availability;
            Appointments = appointments;
            Schedule = schedule;
            Clock = clock;
            DoctorId = doctorId;
            DoctorUserId = doctorUserId;
            OtherDoctorUserId = otherDoctorUserId;
            ReceptionUserId = receptionUserId;
        }

        public WorkingHourExtensionService Service { get; }
        public DoctorAvailabilityService Availability { get; }
        public InMemoryAppointmentRepository Appointments { get; }
        public InMemoryDoctorScheduleRepository Schedule { get; }
        public FixedClock Clock { get; }
        public Guid DoctorId { get; }
        public Guid DoctorUserId { get; }
        public Guid OtherDoctorUserId { get; }
        public Guid ReceptionUserId { get; }

        public static Fixture Create(DateOnly? today = null, TimeOnly? secondWindowStart = null)
        {
            var appointments = new InMemoryAppointmentRepository();
            var schedule = new InMemoryDoctorScheduleRepository();
            var unitOfWork = new InMemoryUnitOfWork();
            var doctorUserId = Guid.NewGuid();
            var doctor = ScheduleTestData.NewDoctor(doctorUserId);
            var otherUserId = Guid.NewGuid();
            appointments.AddDoctor(doctor);
            appointments.AddDoctor(ScheduleTestData.NewDoctor(otherUserId));
            ScheduleTestData.AddWeeklyWindow(appointments, doctor.Id, Date, new TimeOnly(9, 0), WindowEnd);
            if (secondWindowStart is TimeOnly start)
                ScheduleTestData.AddWeeklyWindow(appointments, doctor.Id, Date, start, new TimeOnly(12, 0));

            var clock = new FixedClock(new DateTimeOffset(Date.AddDays(-1).ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero), today ?? Date.AddDays(-1));
            var availability = new DoctorAvailabilityService(appointments, schedule, clock);
            var service = new WorkingHourExtensionService(appointments, schedule, unitOfWork, clock);
            return new Fixture(service, availability, appointments, schedule, clock, doctor.Id, doctorUserId, otherUserId, Guid.NewGuid());
        }
    }
}
