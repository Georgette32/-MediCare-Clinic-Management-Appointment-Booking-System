using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Phase 2 leftover: Confirmed appointments whose day has passed are resolved by maintenance.</summary>
public sealed class PreviousDayAppointmentResolverTests
{
    private static readonly DateOnly Today = QueueTestData.Today;
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Fact]
    public async Task Previous_day_appointment_on_an_open_worklist_is_cancelled_as_clinic_unavailable()
    {
        var w = new World(At(10, 5));
        var appointment = w.Confirmed(Yesterday, new TimeOnly(9, 0));
        w.Worklist.Add(appointment.Id);

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, result.ClinicUnavailable);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, appointment.CancellationReason);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Previous_day_appointment_never_checked_in_becomes_no_show_and_never_clinic_unavailable()
    {
        var w = new World(At(10, 5));
        var appointment = w.Confirmed(Yesterday, new TimeOnly(9, 0));

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, result.NoShow);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Null(appointment.CancellationReason);
    }

    [Fact]
    public async Task Previous_day_appointment_whose_attendance_was_cancelled_is_cancelled_as_patient_left()
    {
        var w = new World(At(10, 5));
        var appointment = w.Confirmed(Yesterday, new TimeOnly(9, 0));
        var attempt = w.Attempt(appointment);
        attempt.Cancel(attempt.ArrivedAt.AddMinutes(5));

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, result.PatientLeft);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("patient left before consultation", appointment.CancellationReason);
        Assert.Equal(AppointmentCancellationReasons.PatientLeftBeforeConsultation, appointment.CancellationReason);
    }

    [Fact]
    public async Task Previous_day_appointment_with_a_completed_attempt_is_left_alone()
    {
        var w = new World(At(10, 5));
        var appointment = w.Confirmed(Yesterday, new TimeOnly(9, 0));
        var attempt = w.Attempt(appointment);
        attempt.Call(At(9, 0));
        attempt.StartConsultation(At(9, 2));
        attempt.Complete(At(9, 20));

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
    }

    [Fact]
    public async Task Previous_day_appointment_with_a_live_attempt_is_left_for_the_stale_sweep()
    {
        var w = new World(At(10, 5));
        var waiting = w.Confirmed(Yesterday, new TimeOnly(9, 0));
        w.Attempt(waiting);
        var called = w.Confirmed(Yesterday, new TimeOnly(9, 30));
        w.Attempt(called).Call(At(9, 35));

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(2, result.Skipped);
        Assert.Equal(AppointmentStatus.Confirmed, waiting.Status);
        Assert.Equal(AppointmentStatus.Confirmed, called.Status);
    }

    [Fact]
    public async Task Todays_and_future_appointments_are_not_touched()
    {
        var w = new World(At(10, 5));
        var today = w.Confirmed(Today, new TimeOnly(9, 0));
        var future = w.Confirmed(Today.AddDays(1), new TimeOnly(9, 0));
        w.Worklist.Add(today.Id);

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(0, result.ClinicUnavailable + result.NoShow + result.PatientLeft + result.Skipped);
        Assert.Equal(AppointmentStatus.Confirmed, today.Status);
        Assert.Equal(AppointmentStatus.Confirmed, future.Status);
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var w = new World(At(10, 5));
        var noShow = w.Confirmed(Yesterday, new TimeOnly(9, 0));
        var left = w.Confirmed(Yesterday, new TimeOnly(9, 30));
        var leftAttempt = w.Attempt(left);
        leftAttempt.Cancel(leftAttempt.ArrivedAt.AddMinutes(5));

        var first = await w.Resolver.ResolveAsync(CancellationToken.None);
        var second = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, first.NoShow);
        Assert.Equal(1, first.PatientLeft);
        Assert.Equal(0, second.NoShow + second.PatientLeft + second.ClinicUnavailable);
        Assert.Equal(AppointmentStatus.NoShow, noShow.Status);
        Assert.Equal(AppointmentStatus.Cancelled, left.Status);
    }

    [Fact]
    public async Task A_failing_appointment_is_reported_and_does_not_stop_the_others()
    {
        var w = new World(At(10, 5));
        var broken = w.Confirmed(Yesterday, new TimeOnly(9, 0));
        var fine = w.Confirmed(Yesterday, new TimeOnly(9, 30));
        w.PreviousDay.FailingAppointments.Add(broken.Id);

        var result = await w.Resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal(1, result.NoShow);
        var failure = Assert.Single(result.Failures);
        Assert.Contains(broken.Id.ToString("D"), failure);
        Assert.Equal(AppointmentStatus.NoShow, fine.Status);
        Assert.Equal(AppointmentStatus.Confirmed, broken.Status);
    }

    private static DateTimeOffset At(int hour, int minute) => new(Today.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private sealed class World
    {
        public World(DateTimeOffset now)
        {
            Clock = new FixedClock(now, Today);
            Doctor = QueueTestData.NewDoctor();
            Appointments.AddDoctor(Doctor);
            PreviousDay = new InMemoryPreviousDayRepository(Appointments, Queue);
            Disruptions = new NoopDisruptionService(id =>
            {
                if (!Worklist.Contains(id))
                    return false;
                Appointments.Appointments[id].CancelByClinic(AppointmentCancellationReasons.ClinicUnavailable, Clock.UtcNow);
                return true;
            });
            var appointmentService = new AppointmentService(Appointments, UnitOfWork, Clock, Queue,
                new DoctorAvailabilityService(Appointments, new InMemoryDoctorScheduleRepository(), Clock));
            Resolver = new PreviousDayAppointmentResolver(PreviousDay, Appointments, Disruptions, appointmentService, UnitOfWork, Clock);
        }

        public FixedClock Clock { get; }
        public Doctor Doctor { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public InMemoryUnitOfWork UnitOfWork { get; } = new();
        public InMemoryPreviousDayRepository PreviousDay { get; }
        public NoopDisruptionService Disruptions { get; }
        public HashSet<Guid> Worklist { get; } = [];
        public PreviousDayAppointmentResolver Resolver { get; }

        public Appointment Confirmed(DateOnly date, TimeOnly start)
        {
            var patient = QueueTestData.NewPatient("Patient " + start.ToString("HHmm"));
            var appointment = Appointment.Request(patient.Id, Doctor.Id, date, start, start.AddMinutes(20), At(7, 0));
            appointment.Confirm(At(7, 0));
            Appointments.Appointments[appointment.Id] = appointment;
            QueueTestData.LinkAppointment(appointment, patient);
            Patients[appointment.Id] = patient;
            return appointment;
        }

        /// <summary>A scheduled attendance attempt for the appointment, left in Waiting unless the test moves it on.</summary>
        public QueueEntry Attempt(Appointment appointment)
        {
            var patient = Patients[appointment.Id];
            var entry = QueueEntry.CreateScheduled(patient.Id, Doctor.Id, appointment.Id, appointment.AppointmentDate, At(8, 55).AddDays(appointment.AppointmentDate.DayNumber - Today.DayNumber));
            QueueTestData.Link(entry, patient, appointment);
            Queue.Add(entry);
            return entry;
        }

        private Dictionary<Guid, Patient> Patients { get; } = [];
    }
}

internal sealed class InMemoryPreviousDayRepository(InMemoryAppointmentRepository appointments, InMemoryQueueRepository queue) : IPreviousDayAppointmentRepository
{
    public HashSet<Guid> FailingAppointments { get; } = [];

    public Task<IReadOnlyList<Guid>> GetConfirmedBeforeDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(appointments.Appointments.Values
            .Where(item => item.Status == AppointmentStatus.Confirmed && item.AppointmentDate < date)
            .OrderBy(item => item.AppointmentDate)
            .ThenBy(item => item.StartTime)
            .Select(item => item.Id)
            .ToArray());

    public Task<IReadOnlyList<QueueEntryStatus>> GetAttemptStatusesAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        if (FailingAppointments.Contains(appointmentId))
            throw new InvalidOperationException("boom");

        return Task.FromResult<IReadOnlyList<QueueEntryStatus>>(queue.Entries.Values
            .Where(item => item.AppointmentId == appointmentId)
            .Select(item => item.Status)
            .ToArray());
    }
}
