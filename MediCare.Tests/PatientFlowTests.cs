using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Time;
using MediCare.Web.Models;

namespace MediCare.Tests;

public sealed class PatientFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 30);
    private static readonly DateOnly AppointmentDay = new(2026, 12, 1);

    [Fact]
    public void Patient_cancel_and_reschedule_stop_at_the_clinic_day_boundary()
    {
        // The appointment day begins at 00:00 Africa/Cairo. The instant one minute earlier is still the previous day.
        var zone = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
        var provider = new FlowTimeProvider(Now);
        var clinic = new SystemClinicClock(provider, zone);
        var dayStart = clinic.ToUtc(AppointmentDay, TimeOnly.MinValue);

        provider.UtcNow = dayStart.AddMinutes(-1);
        Assert.True(AppointmentPolicy.CanPatientCancel(AppointmentStatus.Confirmed, AppointmentDay, clinic.Today));
        Assert.True(AppointmentPolicy.CanPatientReschedule(AppointmentStatus.Confirmed, AppointmentDay, clinic.Today, hasLiveReplacement: false));

        provider.UtcNow = dayStart;
        Assert.Equal(AppointmentDay, clinic.Today);
        Assert.False(AppointmentPolicy.CanPatientCancel(AppointmentStatus.Confirmed, AppointmentDay, clinic.Today));
        Assert.False(AppointmentPolicy.CanPatientReschedule(AppointmentStatus.Confirmed, AppointmentDay, clinic.Today, hasLiveReplacement: false));

        // The domain enforces the same cutoff, so the buttons and the server agree.
        var appointment = Appointment.Request(Guid.NewGuid(), Guid.NewGuid(), AppointmentDay, new TimeOnly(9, 0), new TimeOnly(9, 20), Now);
        appointment.Confirm(Now);
        Assert.Throws<InvalidOperationException>(() => appointment.CancelByPatient(clinic.Today, Now));
    }

    [Fact]
    public void Action_flags_hide_reschedule_when_a_live_replacement_exists_and_show_clinic_actions_on_the_day()
    {
        Assert.False(AppointmentPolicy.CanPatientReschedule(AppointmentStatus.Confirmed, AppointmentDay, Today, hasLiveReplacement: true));
        Assert.False(AppointmentPolicy.CanPatientReschedule(AppointmentStatus.Pending, AppointmentDay, Today, hasLiveReplacement: false));
        Assert.True(AppointmentPolicy.CanClinicCancel(AppointmentStatus.Pending, AppointmentDay, AppointmentDay));
        Assert.False(AppointmentPolicy.CanClinicCancel(AppointmentStatus.Confirmed, AppointmentDay, AppointmentDay.AddDays(1)));
        Assert.False(AppointmentPolicy.CanClinicReschedule(AppointmentStatus.Confirmed, AppointmentDay, AppointmentDay, hasLiveReplacement: true));
    }

    [Fact]
    public async Task Patient_cannot_cancel_reschedule_or_read_another_patients_appointment()
    {
        var flow = CreateFlow();
        var appointment = AddConfirmed(flow, flow.PatientId, new TimeOnly(9, 0));
        var other = flow.OtherPatientId;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            flow.Appointments.CancelByPatientAsync(appointment.Id, other, null, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            flow.Appointments.RescheduleAsync(appointment.Id, other, flow.DoctorId, AppointmentDay, new TimeOnly(10, 0), CancellationToken.None));
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);

        flow.Query.Appointments.Add(ToRecord(appointment));
        Assert.Null(await flow.Queries.GetOwnedAppointmentAsync(appointment.Id, other, CancellationToken.None));
        Assert.NotNull(await flow.Queries.GetOwnedAppointmentAsync(appointment.Id, flow.PatientId, CancellationToken.None));
    }

    [Fact]
    public async Task Booking_failure_returns_the_doctors_other_free_slots_for_that_date()
    {
        var flow = CreateFlow();
        AddConfirmed(flow, flow.PatientId, new TimeOnly(9, 0));

        var outcome = await flow.Booking.RequestForPatientAsync(flow.OtherPatientId, flow.DoctorId, AppointmentDay, new TimeOnly(9, 0), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("already reserved", outcome.Error);
        Assert.Contains(new TimeOnly(9, 20), outcome.Alternatives);
        Assert.DoesNotContain(new TimeOnly(9, 0), outcome.Alternatives);
    }

    [Fact]
    public async Task Patient_request_is_pending_and_past_times_are_refused()
    {
        var flow = CreateFlow();

        var created = await flow.Booking.RequestForPatientAsync(flow.PatientId, flow.DoctorId, AppointmentDay, new TimeOnly(9, 20), CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.Equal(AppointmentStatus.Pending, flow.Repo.Items[created.AppointmentId!.Value].Status);

        // Today's 09:00 is already in the past at 12:00 clinic time.
        var past = await flow.Booking.RequestForPatientAsync(flow.PatientId, flow.DoctorId, Today, new TimeOnly(9, 0), CancellationToken.None);
        Assert.False(past.Succeeded);
        Assert.Contains("already passed", past.Error);
    }

    [Fact]
    public async Task Patient_reschedule_to_the_same_doctor_date_and_start_is_rejected_with_the_rule_message()
    {
        var flow = CreateFlow();
        var appointment = AddConfirmed(flow, flow.PatientId, new TimeOnly(9, 0));

        var outcome = await flow.Booking.RescheduleForPatientAsync(appointment.Id, flow.PatientId, flow.DoctorId, AppointmentDay, new TimeOnly(9, 0), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("different doctor, appointment date, or start time", outcome.Error);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
    }

    [Fact]
    public async Task Clinic_booking_is_created_confirmed_in_one_transaction()
    {
        var flow = CreateFlow();

        var outcome = await flow.Booking.BookForClinicAsync(flow.PatientId, flow.DoctorId, AppointmentDay, new TimeOnly(9, 40), ClinicCancellationActor.Reception, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(AppointmentStatus.Confirmed, flow.Repo.Items[outcome.AppointmentId!.Value].Status);
        Assert.Equal(1, flow.Uow.CommitCount);
    }

    [Fact]
    public async Task Clinic_booking_respects_existing_appointments_and_breaks()
    {
        var flow = CreateFlow();
        AddConfirmed(flow, flow.PatientId, new TimeOnly(9, 0));
        flow.Schedule.Breaks.Add(MediCare.Domain.Entities.DoctorBreak.Create(flow.DoctorId, AppointmentDay, new TimeOnly(10, 0), new TimeOnly(10, 20)));

        var conflict = await flow.Booking.BookForClinicAsync(flow.OtherPatientId, flow.DoctorId, AppointmentDay, new TimeOnly(9, 0), ClinicCancellationActor.Admin, CancellationToken.None);
        var breakOverlap = await flow.Booking.BookForClinicAsync(flow.OtherPatientId, flow.DoctorId, AppointmentDay, new TimeOnly(10, 0), ClinicCancellationActor.Admin, CancellationToken.None);

        Assert.False(conflict.Succeeded);
        Assert.Contains("already reserved", conflict.Error);
        Assert.False(breakOverlap.Succeeded);
        Assert.Contains("break", breakOverlap.Error);
        Assert.Single(flow.Repo.Items);
    }

    [Fact]
    public async Task Clinic_booking_uses_approved_working_hour_extensions()
    {
        // The doctor works 09:00-10:00 only; 10:20 needs an approved extension to 10:40.
        var flow = CreateFlow(windowEnd: new TimeOnly(10, 0));
        var before = await flow.Booking.BookForClinicAsync(flow.PatientId, flow.DoctorId, AppointmentDay, new TimeOnly(10, 20), ClinicCancellationActor.Reception, CancellationToken.None);
        Assert.False(before.Succeeded);

        var extension = MediCare.Domain.Entities.DoctorWorkingHourExtension.CreateApprovedByDoctor(
            flow.DoctorId, AppointmentDay, new TimeOnly(10, 0), new TimeOnly(10, 40), "Clinic running late", Guid.NewGuid(), Now);
        flow.Schedule.Extensions[extension.Id] = extension;

        var after = await flow.Booking.BookForClinicAsync(flow.PatientId, flow.DoctorId, AppointmentDay, new TimeOnly(10, 20), ClinicCancellationActor.Reception, CancellationToken.None);
        Assert.True(after.Succeeded);
        Assert.Equal(AppointmentStatus.Confirmed, flow.Repo.Items[after.AppointmentId!.Value].Status);
    }

    [Fact]
    public async Task Clinic_cancellation_requires_a_reason_and_a_clinic_actor()
    {
        var flow = CreateFlow();
        var appointment = AddConfirmed(flow, flow.PatientId, new TimeOnly(9, 0));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            flow.Appointments.CancelByClinicAsync(appointment.Id, ClinicCancellationActor.Reception, "   ", CancellationToken.None));
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);

        await flow.Appointments.CancelByClinicAsync(appointment.Id, ClinicCancellationActor.Admin, "Doctor called in sick", CancellationToken.None);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("Doctor called in sick", appointment.CancellationReason);
    }

    [Fact]
    public void Clinic_actor_comes_from_the_signed_in_role()
    {
        Assert.Equal(ClinicCancellationActor.Admin, ClinicActorResolver.From(Principal(MediCareRoles.Admin)));
        Assert.Equal(ClinicCancellationActor.Reception, ClinicActorResolver.From(Principal(MediCareRoles.Receptionist)));
        Assert.Throws<UnauthorizedAccessException>(() => ClinicActorResolver.From(Principal(MediCareRoles.Patient)));
    }

    [Fact]
    public async Task Patient_search_normalizes_phone_numbers_and_needs_enough_digits()
    {
        var flow = CreateFlow();

        await flow.Queries.SearchPatientsAsync("0101 234 5678", CancellationToken.None);
        Assert.Equal(("+201012345678", (string?)null, (string?)null), flow.Query.LastSearch);

        await flow.Queries.SearchPatientsAsync("1012", CancellationToken.None);
        Assert.Equal(((string?)null, "1012", (string?)null), flow.Query.LastSearch);

        await flow.Queries.SearchPatientsAsync("Sara", CancellationToken.None);
        Assert.Equal(((string?)null, (string?)null, "Sara"), flow.Query.LastSearch);

        flow.Query.LastSearch = null;
        await flow.Queries.SearchPatientsAsync("01", CancellationToken.None);
        Assert.Null(flow.Query.LastSearch);
    }

    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(role => new Claim(ClaimTypes.Role, role)), "test"));

    private static AppointmentRecord ToRecord(Appointment appointment) =>
        new(appointment.Id, appointment.PatientId, "Patient", "+201012345678", appointment.DoctorId, "Dr. Hala", "Cardiology",
            appointment.AppointmentDate, appointment.StartTime, appointment.EndTime, appointment.Status, null, null, null, null);

    private static Appointment AddConfirmed(Flow flow, Guid patientId, TimeOnly start)
    {
        var appointment = Appointment.Request(patientId, flow.DoctorId, AppointmentDay, start, start.AddMinutes(20), Now);
        appointment.Confirm(Now);
        flow.Repo.Items.Add(appointment.Id, appointment);
        flow.Schedule.Booked.Add((flow.DoctorId, AppointmentDay, start, start.AddMinutes(20)));
        return appointment;
    }

    private static Flow CreateFlow(TimeOnly windowEnd = default)
    {
        var schedule = new InMemoryDoctorScheduleRepository();
        var repo = new FlowAppointmentRepository(schedule);
        var patientId = Guid.NewGuid();
        var otherPatientId = Guid.NewGuid();
        repo.PatientIds.Add(patientId);
        repo.PatientIds.Add(otherPatientId);

        var doctorId = Guid.NewGuid();
        repo.Doctors.Add(doctorId, new Doctor(doctorId, Guid.NewGuid(), "Dr. Hala", Guid.NewGuid(), 20));
        var end = windowEnd == default ? new TimeOnly(11, 0) : windowEnd;
        foreach (var day in Enum.GetValues<DayOfWeek>())
            repo.WorkingHours.Add(DoctorWorkingHour.Create(doctorId, day, new TimeOnly(9, 0), end));

        var clock = new FlowClock(Now, Today);
        var uow = new FlowUnitOfWork();
        var availability = new DoctorAvailabilityService(repo, schedule, clock);
        var appointments = new AppointmentService(repo, uow, clock, new InMemoryQueueRepository(), availability);
        var query = new FlowQueryRepository();
        return new Flow(
            clock,
            uow,
            schedule,
            repo,
            query,
            appointments,
            new AppointmentBookingFlow(appointments, availability),
            new AppointmentQueryService(query, clock),
            patientId,
            otherPatientId,
            doctorId);
    }

    private sealed record Flow(
        FlowClock Clock,
        FlowUnitOfWork Uow,
        InMemoryDoctorScheduleRepository Schedule,
        FlowAppointmentRepository Repo,
        FlowQueryRepository Query,
        AppointmentService Appointments,
        AppointmentBookingFlow Booking,
        AppointmentQueryService Queries,
        Guid PatientId,
        Guid OtherPatientId,
        Guid DoctorId);
}
