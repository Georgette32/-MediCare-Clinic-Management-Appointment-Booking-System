using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>
/// A doctor who left early cannot be called from the disruption's from-time, new walk-ins are refused for that day, and
/// dated breaks block calls while they last. Covers scheduled Call, Call Next and walk-in registration.
/// </summary>
public sealed class DoctorCallWindowTests
{
    private static readonly DateOnly Today = QueueTestData.Today;

    [Fact]
    public async Task Scheduled_call_is_refused_once_the_doctor_has_left_early_with_the_worklist_message()
    {
        var s = new QueueScenario();
        var entry = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Waiting"), new TimeOnly(9, 30)));
        s.AddDisruption(new TimeOnly(8, 30));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CallScheduledAsync(entry.Id, CancellationToken.None));

        Assert.Equal("Dr. Test left at 08:30; use the disruption worklist.", exception.Message);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task Scheduled_call_is_allowed_before_the_disruption_from_time()
    {
        var s = new QueueScenario();
        s.Clock.UtcNow = new DateTimeOffset(Today.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
        var entry = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Waiting"), new TimeOnly(8, 30)));
        s.AddDisruption(new TimeOnly(8, 30));

        await s.Service.CallScheduledAsync(entry.Id, CancellationToken.None);

        Assert.Equal(QueueEntryStatus.Called, entry.Status);
    }

    [Fact]
    public async Task Call_next_is_refused_after_the_doctor_left_early_even_for_an_approved_walk_in()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.Approve(walkIn);
        s.AddDisruption(new TimeOnly(8, 45));

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Contains("left at 08:45", result.Message);
        Assert.Equal(QueueEntryStatus.Waiting, walkIn.Status);
    }

    [Fact]
    public async Task Call_next_and_scheduled_call_are_refused_during_a_dated_break()
    {
        var s = new QueueScenario();
        var entry = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Waiting"), new TimeOnly(9, 30)));
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.Approve(walkIn);
        s.Schedule.Breaks.Add(DoctorBreak.Create(s.Doctor.Id, Today, new TimeOnly(8, 50), new TimeOnly(9, 15)));

        var next = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);
        var scheduled = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CallScheduledAsync(entry.Id, CancellationToken.None));

        Assert.False(next.Called);
        Assert.Contains("scheduled break", next.Message);
        Assert.Contains("scheduled break", scheduled.Message);
        Assert.Equal(QueueEntryStatus.Waiting, walkIn.Status);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task Call_next_resumes_once_the_dated_break_has_ended()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.Approve(walkIn);
        s.Schedule.Breaks.Add(DoctorBreak.Create(s.Doctor.Id, Today, new TimeOnly(8, 50), new TimeOnly(9, 0)));

        var result = await s.Service.CallNextAsync(s.Doctor.Id, CancellationToken.None);

        Assert.True(result.Called);
        Assert.Equal(QueueEntryStatus.Called, walkIn.Status);
    }

    [Fact]
    public async Task Walk_in_registration_is_refused_after_the_doctor_left_early()
    {
        var fixture = new RegistrationFixture(new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
        fixture.AddDisruption(new TimeOnly(8, 30));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CheckIn.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None));

        Assert.Contains("left at 08:30", exception.Message);
        Assert.Empty(fixture.Queue.Entries);
    }

    [Fact]
    public async Task Walk_in_registration_converts_an_expired_grace_period_to_no_show_before_registering()
    {
        // Confirmed 09:00 with a 20-minute expected duration -> 60-minute grace, expiring at 10:00. Arrival at 10:05.
        var fixture = new RegistrationFixture(new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 5)), TimeSpan.Zero));
        var appointment = Appointment.Request(fixture.PatientId, fixture.DoctorId, Today, new TimeOnly(9, 0), new TimeOnly(9, 20),
            fixture.Clock.UtcNow.AddHours(-2));
        appointment.Confirm(fixture.Clock.UtcNow.AddHours(-2));
        fixture.Appointments.Appointments[appointment.Id] = appointment;

        var walkInId = await fixture.CheckIn.RegisterWalkInAsync(fixture.PatientId, fixture.DoctorId, CancellationToken.None);

        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        var entry = Assert.Single(fixture.Queue.Entries.Values);
        Assert.Equal(walkInId, entry.Id);
        Assert.Equal(QueueEntrySource.WalkIn, entry.Source);
        Assert.Null(entry.AppointmentId);
    }

    private sealed class RegistrationFixture
    {
        public RegistrationFixture(DateTimeOffset now)
        {
            Clock = new FixedClock(now, Today);
            Appointments = new InMemoryAppointmentRepository();
            Queue = new InMemoryQueueRepository();
            Disruptions = new InMemoryDoctorDisruptionRepository();
            Schedule = new InMemoryDoctorScheduleRepository();
            var doctor = QueueTestData.NewDoctor();
            DoctorId = doctor.Id;
            Appointments.AddDoctor(doctor);
            PatientId = Guid.NewGuid();
            var unitOfWork = new InMemoryUnitOfWork();
            var callWindows = new DoctorCallWindowService(Appointments, Disruptions, Schedule);
            var grace = new GracePeriodService(Appointments, Queue, new FixedExpectedDuration(20), Disruptions, unitOfWork, Clock);
            CheckIn = new CheckInService(Appointments, Queue, new FixedExpectedDuration(20), Disruptions, unitOfWork, Clock, grace, callWindows);
        }

        public FixedClock Clock { get; }
        public InMemoryAppointmentRepository Appointments { get; }
        public InMemoryQueueRepository Queue { get; }
        public InMemoryDoctorDisruptionRepository Disruptions { get; }
        public InMemoryDoctorScheduleRepository Schedule { get; }
        public CheckInService CheckIn { get; }
        public Guid DoctorId { get; }
        public Guid PatientId { get; }

        public void AddDisruption(TimeOnly fromTime)
        {
            var disruption = DoctorDisruption.Record(DoctorId, Today, fromTime, "Emergency", Guid.NewGuid(), Clock.UtcNow);
            Disruptions.Disruptions[disruption.Id] = disruption;
        }
    }
}
