using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

public sealed class DoctorAvailabilityTests
{
    private static readonly DateOnly Date = new(2026, 12, 2);
    private static readonly TimeOnly WindowStart = new(9, 0);
    private static readonly TimeOnly WindowEnd = new(11, 0);

    [Fact]
    public async Task Without_extension_slots_stop_at_window_end()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));

        var slots = await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None);

        Assert.Equal(new TimeOnly[] { new(9, 0), new(9, 20), new(9, 40), new(10, 0), new(10, 20), new(10, 40) }, slots);
    }

    [Fact]
    public async Task Approved_extension_offers_later_slots_inside_the_extended_window()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.AddExtension(WorkingHourExtensionStatus.Approved, new TimeOnly(11, 40));

        var windows = await s.Availability.GetEffectiveWindowsAsync(s.DoctorId, Date, CancellationToken.None);
        var slots = await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None);

        var window = Assert.Single(windows);
        Assert.Equal(new TimeOnly(11, 40), window.EndTime);
        Assert.Equal(WindowEnd, window.ConfiguredEndTime);
        Assert.NotNull(window.ExtensionId);
        Assert.Contains(new TimeOnly(11, 0), slots);
        Assert.Contains(new TimeOnly(11, 20), slots);
        Assert.DoesNotContain(new TimeOnly(11, 40), slots);
    }

    [Theory]
    [InlineData(WorkingHourExtensionStatus.Pending)]
    [InlineData(WorkingHourExtensionStatus.Rejected)]
    [InlineData(WorkingHourExtensionStatus.Cancelled)]
    public async Task Extension_that_is_not_approved_does_not_add_availability(WorkingHourExtensionStatus status)
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.AddExtension(status, new TimeOnly(11, 40));

        var windows = await s.Availability.GetEffectiveWindowsAsync(s.DoctorId, Date, CancellationToken.None);
        var slots = await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None);

        Assert.Equal(WindowEnd, Assert.Single(windows).EndTime);
        Assert.DoesNotContain(new TimeOnly(11, 0), slots);
    }

    [Fact]
    public async Task Holiday_removes_every_window_and_slot()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.AddExtension(WorkingHourExtensionStatus.Approved, new TimeOnly(11, 40));
        s.Appointments.Holidays.Add(DoctorHoliday.Create(s.DoctorId, Date, Date, "Conference"));

        Assert.Empty(await s.Availability.GetEffectiveWindowsAsync(s.DoctorId, Date, CancellationToken.None));
        Assert.Empty(await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None));
    }

    [Fact]
    public async Task Dated_break_blocks_overlapping_slots_only()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.Schedule.Breaks.Add(DoctorBreak.Create(s.DoctorId, Date, new TimeOnly(10, 0), new TimeOnly(10, 30)));

        var slots = await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None);

        Assert.Equal(new TimeOnly[] { new(9, 0), new(9, 20), new(9, 40), new(10, 40) }, slots);
    }

    [Fact]
    public async Task Booked_appointment_blocks_its_slot()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.Schedule.Booked.Add((s.DoctorId, Date, new TimeOnly(9, 20), new TimeOnly(9, 40)));

        var slots = await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None);

        Assert.DoesNotContain(new TimeOnly(9, 20), slots);
        Assert.Contains(new TimeOnly(9, 40), slots);
    }

    [Fact]
    public async Task Today_excludes_starts_before_the_current_clinic_time()
    {
        var s = Scenario.Create(today: Date, now: new TimeOnly(10, 10));

        var slots = await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None);

        Assert.Equal(new TimeOnly[] { new(10, 20), new(10, 40) }, slots);
    }

    [Fact]
    public async Task Past_dates_offer_no_slots()
    {
        var s = Scenario.Create(today: Date.AddDays(1));

        Assert.Empty(await s.Availability.GetAvailableSlotsAsync(s.DoctorId, Date, CancellationToken.None));
    }

    [Fact]
    public async Task Booking_inside_an_approved_extension_succeeds_and_ends_at_the_extended_slot_end()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.AddExtension(WorkingHourExtensionStatus.Approved, new TimeOnly(11, 40));

        var appointmentId = await s.Booking.RequestAsync(s.PatientId, s.DoctorId, Date, new TimeOnly(11, 20), CancellationToken.None);

        var appointment = s.Appointments.Appointments[appointmentId];
        Assert.Equal(new TimeOnly(11, 40), appointment.EndTime);
    }

    [Fact]
    public async Task Booking_outside_an_approved_extension_fails_and_pending_extension_does_not_help()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.AddExtension(WorkingHourExtensionStatus.Pending, new TimeOnly(11, 40));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.Booking.RequestAsync(s.PatientId, s.DoctorId, Date, new TimeOnly(11, 20), CancellationToken.None));
        Assert.Contains("outside the doctor's configured working schedule", error.Message);
    }

    [Fact]
    public async Task Booking_that_runs_past_the_extended_end_is_rejected()
    {
        var s = Scenario.Create(today: Date.AddDays(-1));
        s.AddExtension(WorkingHourExtensionStatus.Approved, new TimeOnly(11, 10));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.Booking.RequestAsync(s.PatientId, s.DoctorId, Date, new TimeOnly(11, 0), CancellationToken.None));
        Assert.Contains("would extend beyond the doctor's working hours", error.Message);
    }

    [Fact]
    public async Task Walk_in_call_inside_an_approved_extension_is_allowed()
    {
        var s = Scenario.Create(today: Date, now: new TimeOnly(11, 20));
        s.AddExtension(WorkingHourExtensionStatus.Approved, new TimeOnly(11, 40));
        var (entryId, approvalId) = s.AddApprovedWalkIn();

        var result = await s.WalkIns.CallAsync(entryId, approvalId, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(QueueEntryStatus.Called, s.Queue.Entries[entryId].Status);
    }

    [Fact]
    public async Task Walk_in_call_outside_working_hours_is_refused_without_an_approved_extension()
    {
        var s = Scenario.Create(today: Date, now: new TimeOnly(11, 20));
        s.AddExtension(WorkingHourExtensionStatus.Pending, new TimeOnly(11, 40));
        var (entryId, approvalId) = s.AddApprovedWalkIn();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.WalkIns.CallAsync(entryId, approvalId, CancellationToken.None));

        Assert.Contains("outside configured working hours", error.Message);
        Assert.Equal(QueueEntryStatus.Waiting, s.Queue.Entries[entryId].Status);
    }

    private sealed class Scenario
    {
        private Scenario(
            DoctorAvailabilityService availability,
            AppointmentService booking,
            WalkInAdmissionApprovalService walkIns,
            InMemoryAppointmentRepository appointments,
            InMemoryDoctorScheduleRepository schedule,
            InMemoryQueueRepository queue,
            InMemoryApprovalRepository approvals,
            FixedClock clock,
            Guid doctorId,
            Guid patientId)
        {
            Availability = availability;
            Booking = booking;
            WalkIns = walkIns;
            Appointments = appointments;
            Schedule = schedule;
            Queue = queue;
            Approvals = approvals;
            Clock = clock;
            DoctorId = doctorId;
            PatientId = patientId;
        }

        public DoctorAvailabilityService Availability { get; }
        public AppointmentService Booking { get; }
        public WalkInAdmissionApprovalService WalkIns { get; }
        public InMemoryAppointmentRepository Appointments { get; }
        public InMemoryDoctorScheduleRepository Schedule { get; }
        public InMemoryQueueRepository Queue { get; }
        public InMemoryApprovalRepository Approvals { get; }
        public FixedClock Clock { get; }
        public Guid DoctorId { get; }
        public Guid PatientId { get; }

        public static Scenario Create(DateOnly today, TimeOnly? now = null)
        {
            var appointments = new InMemoryAppointmentRepository();
            var schedule = new InMemoryDoctorScheduleRepository();
            var queue = new InMemoryQueueRepository();
            var approvals = new InMemoryApprovalRepository();
            var unitOfWork = new InMemoryUnitOfWork();
            var doctor = ScheduleTestData.NewDoctor();
            appointments.AddDoctor(doctor);
            ScheduleTestData.AddWeeklyWindow(appointments, doctor.Id, Date, WindowStart, WindowEnd);

            var clockTime = now ?? new TimeOnly(8, 0);
            var clock = new FixedClock(new DateTimeOffset(today.ToDateTime(clockTime), TimeSpan.Zero), today);
            var availability = new DoctorAvailabilityService(appointments, schedule, clock);
            var booking = new AppointmentService(appointments, unitOfWork, clock, queue, availability);
            var walkIns = new WalkInAdmissionApprovalService(queue, appointments, approvals, unitOfWork, clock,
                Options.Create(new WalkInAdmissionApprovalOptions()), availability,
                new DoctorCallWindowService(appointments, new InMemoryDoctorDisruptionRepository(), schedule));
            return new Scenario(availability, booking, walkIns, appointments, schedule, queue, approvals, clock, doctor.Id, Guid.NewGuid());
        }

        /// <summary>Records an extension of the 11:00 window end on the test date with the given status.</summary>
        public void AddExtension(WorkingHourExtensionStatus status, TimeOnly extendedEnd)
        {
            var now = Clock.UtcNow;
            var extension = status switch
            {
                WorkingHourExtensionStatus.Approved => ScheduleTestData.ApprovedExtension(DoctorId, Date, WindowEnd, extendedEnd, now),
                WorkingHourExtensionStatus.Pending => ScheduleTestData.PendingExtension(DoctorId, Date, WindowEnd, extendedEnd, now),
                WorkingHourExtensionStatus.Rejected => Reject(ScheduleTestData.PendingExtension(DoctorId, Date, WindowEnd, extendedEnd, now), now),
                _ => Cancel(ScheduleTestData.ApprovedExtension(DoctorId, Date, WindowEnd, extendedEnd, now), now),
            };
            Schedule.Extensions[extension.Id] = extension;
        }

        public (Guid EntryId, Guid ApprovalId) AddApprovedWalkIn()
        {
            var arrived = Clock.UtcNow.AddMinutes(-5);
            var entry = QueueEntry.CreateWalkIn(PatientId, DoctorId, Date, arrived, 1);
            Queue.Add(entry);
            var approval = WalkInAdmissionApproval.Request(entry, arrived, Clock.UtcNow.AddHours(1));
            approval.Approve(Clock.UtcNow.AddMinutes(-1));
            Approvals.Items[approval.Id] = approval;
            return (entry.Id, approval.Id);
        }

        private static DoctorWorkingHourExtension Reject(DoctorWorkingHourExtension extension, DateTimeOffset now)
        {
            extension.Reject(now);
            return extension;
        }

        private static DoctorWorkingHourExtension Cancel(DoctorWorkingHourExtension extension, DateTimeOffset now)
        {
            extension.Cancel(now);
            return extension;
        }
    }
}
