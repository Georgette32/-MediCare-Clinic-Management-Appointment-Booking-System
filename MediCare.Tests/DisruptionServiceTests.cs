using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class DisruptionServiceTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Recording_blocks_remaining_time_without_changing_working_hours()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 13);

        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);

        var block = Assert.Single(fixture.Disruptions.Breaks);
        Assert.Equal(new TimeOnly(11, 0), block.StartTime);
        Assert.Equal(new TimeOnly(13, 0), block.EndTime);
        Assert.Equal(disruptionId, block.DisruptionId);
        var hours = Assert.Single(fixture.Appointments.WorkingHours);
        Assert.Equal(new TimeOnly(13, 0), hours.EndTime);
    }

    [Fact]
    public async Task Recording_blocks_the_approved_extension_window()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 13);
        var extension = ScheduleTestData.ApprovedExtension(fixture.Doctor.Id, Today, new TimeOnly(13, 0), new TimeOnly(14, 0), fixture.Clock.UtcNow);
        fixture.Schedule.Extensions[extension.Id] = extension;

        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);

        var block = Assert.Single(fixture.Disruptions.Breaks);
        Assert.Equal(new TimeOnly(11, 0), block.StartTime);
        Assert.Equal(new TimeOnly(14, 0), block.EndTime);
    }

    [Fact]
    public async Task Worklist_includes_affected_patients_and_merges_checked_in_scheduled_patients()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var running = fixture.Confirmed(new TimeOnly(10, 50));
        var starting = fixture.Confirmed(new TimeOnly(11, 0));
        var later = fixture.Confirmed(new TimeOnly(12, 0));
        var earlyCheckedIn = fixture.Confirmed(new TimeOnly(9, 0));
        var earlyEnded = fixture.Confirmed(new TimeOnly(10, 40));
        var completed = fixture.Confirmed(new TimeOnly(9, 20));
        completed.MarkCompleted(fixture.Clock.UtcNow);
        var checkedIn = fixture.CheckIn(earlyCheckedIn);
        var served = fixture.Confirmed(new TimeOnly(11, 20));
        fixture.CheckIn(served).Call(fixture.Clock.UtcNow);
        var walkIn = fixture.WalkIn(1);

        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);

        var items = fixture.Disruptions.Items.Values.Where(item => item.DisruptionId == disruptionId).ToArray();
        var appointmentIds = items.Where(item => item.AppointmentId is not null).Select(item => item.AppointmentId!.Value).ToHashSet();
        Assert.True(new HashSet<Guid> { running.Id, starting.Id, later.Id, earlyCheckedIn.Id }.SetEquals(appointmentIds));
        Assert.DoesNotContain(earlyEnded.Id, appointmentIds);
        Assert.DoesNotContain(completed.Id, appointmentIds);
        Assert.DoesNotContain(served.Id, appointmentIds);

        var merged = items.Single(item => item.AppointmentId == earlyCheckedIn.Id);
        Assert.Equal(checkedIn.Id, merged.QueueEntryId);
        var walkInItem = Assert.Single(items, item => item.AppointmentId is null);
        Assert.Equal(walkIn.Id, walkInItem.QueueEntryId);
        Assert.All(items, item => Assert.Equal(DisruptionOutcome.Pending, item.Outcome));
    }

    [Fact]
    public async Task Duplicate_disruption_for_the_same_doctor_and_date_is_refused()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(12, 0), "Second", Actor, CancellationToken.None));

        Assert.Contains("already recorded", exception.Message);
    }

    [Fact]
    public async Task Disruption_for_a_past_date_is_refused()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(fixture.Doctor.Id, Today.AddDays(-1), new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None));

        Assert.Contains("past date", exception.Message);
    }

    [Fact]
    public async Task Same_day_disruption_with_a_passed_from_time_is_recorded_after_the_fact()
    {
        var fixture = new Fixture(At(12, 0));
        fixture.AddWindow(9, 13);

        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);

        var block = Assert.Single(fixture.Disruptions.Breaks);
        Assert.Equal(new TimeOnly(11, 0), block.StartTime);
    }

    [Fact]
    public async Task Grace_period_does_not_start_or_mark_no_show_for_an_unresolved_item()
    {
        var fixture = new Fixture(At(9, 30));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(9, 0));
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 0), "Emergency", Actor, CancellationToken.None);
        var grace = fixture.GracePeriods();

        var started = await grace.StartGracePeriodIfEligibleAsync(appointment.Id, CancellationToken.None);

        Assert.False(started.StartedSuccessfully);
        Assert.Null(appointment.FrozenGracePeriodMinutes);

        // A grace period that was already frozen and has expired must still not become No-Show while unresolved.
        appointment.StartGracePeriod(20, fixture.Clock.ToUtc(Today, new TimeOnly(9, 0)));
        fixture.Clock.UtcNow = At(10, 0);
        var marked = await grace.ProcessExpiredGracePeriodsForDoctorAsync(fixture.Doctor.Id, Today, CancellationToken.None);

        Assert.Equal(0, marked);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
    }

    [Fact]
    public async Task Unresolved_processing_cancels_only_started_unresolved_appointments_with_the_exact_reason_and_is_idempotent()
    {
        var fixture = new Fixture(At(10, 30));
        fixture.AddWindow(9, 17);
        var started = fixture.Confirmed(new TimeOnly(10, 0));
        var later = fixture.Confirmed(new TimeOnly(11, 0));
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 30), "Emergency", Actor, CancellationToken.None);

        var first = await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);
        var second = await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(AppointmentStatus.Cancelled, started.Status);
        Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, started.CancellationReason);
        Assert.Equal(AppointmentStatus.Confirmed, later.Status);
        var notification = Assert.Single(fixture.Publisher.Published);
        Assert.Equal(PatientNotificationKind.AppointmentCancelledClinicUnavailable, notification.Kind);
        Assert.Equal(started.Id, notification.RelatedAppointmentId);

        // Time passes for the remaining appointment: it is cancelled at its own start, never as No-Show.
        var afterStart = await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.ToUtc(Today, new TimeOnly(11, 0)), CancellationToken.None);
        Assert.Equal(1, afterStart);
        Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, later.CancellationReason);
        Assert.NotEqual(AppointmentStatus.NoShow, later.Status);
    }

    [Fact]
    public async Task Unresolved_processing_leaves_a_patient_already_being_served_alone()
    {
        var fixture = new Fixture(At(10, 30));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(10, 0));
        var entry = fixture.CheckIn(appointment);
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 30), "Emergency", Actor, CancellationToken.None);
        entry.Call(fixture.Clock.UtcNow);

        var processed = await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Empty(fixture.Publisher.Published);
    }

    [Fact]
    public async Task Day_end_cancels_every_unresolved_item_and_notifies_each_patient()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var running = fixture.Confirmed(new TimeOnly(10, 50));
        var starting = fixture.Confirmed(new TimeOnly(11, 0));
        var later = fixture.Confirmed(new TimeOnly(12, 0));
        var served = fixture.Confirmed(new TimeOnly(11, 20));
        var servedEntry = fixture.CheckIn(served);
        var walkIn = fixture.WalkIn(1);
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        servedEntry.Call(fixture.Clock.UtcNow);

        await fixture.Service.RecordDayEndAsync(disruptionId, Actor, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, running.Status);
        Assert.Equal(AppointmentStatus.Cancelled, starting.Status);
        Assert.Equal(AppointmentStatus.Cancelled, later.Status);
        Assert.All(new[] { running, starting, later }, item => Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, item.CancellationReason));
        Assert.Equal(AppointmentStatus.Confirmed, served.Status);
        Assert.Equal(QueueEntryStatus.Cancelled, walkIn.Status);
        Assert.Equal(4, fixture.Publisher.Published.Count);
        Assert.Equal(3, fixture.Publisher.Published.Count(item => item.Kind == PatientNotificationKind.AppointmentCancelledClinicUnavailable));
        Assert.Single(fixture.Publisher.Published, item => item.Kind == PatientNotificationKind.WalkInCancelledClinicUnavailable);
        // The patient already being served needs no staff action, so its item is closed (not cancelled) with a note.
        var openItems = fixture.Disruptions.Items.Values.Where(item => item.DisruptionId == disruptionId && item.IsOpen).ToArray();
        Assert.Empty(openItems);
        var servedItem = fixture.Disruptions.Items.Values.Single(item => item.DisruptionId == disruptionId && item.AppointmentId == served.Id);
        Assert.Equal("Patient is being served.", servedItem.ResolutionNote);
        Assert.True(fixture.Disruptions.Disruptions[disruptionId].IsDayEndRecorded);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordDayEndAsync(disruptionId, Actor, CancellationToken.None));
        Assert.Contains("already recorded", exception.Message);
    }

    [Fact]
    public async Task Reschedule_from_worklist_moves_the_patient_and_marks_the_original_rescheduled()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var otherDoctor = fixture.AddDoctor(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(12, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var item = fixture.ItemFor(disruptionId, appointment.Id);

        var replacementId = await fixture.Service.RescheduleFromWorklistAsync(item.Id, Actor, otherDoctor.Id, Today, new TimeOnly(12, 0), "Moved to colleague", CancellationToken.None);

        var replacement = fixture.Appointments.Appointments[replacementId];
        Assert.Equal(AppointmentStatus.Confirmed, replacement.Status);
        Assert.Equal(otherDoctor.Id, replacement.DoctorId);
        Assert.Equal(appointment.Id, replacement.RescheduledFromAppointmentId);
        Assert.Equal(AppointmentStatus.Rescheduled, appointment.Status);
        Assert.Equal(DisruptionOutcome.Rescheduled, item.Outcome);
        Assert.False(item.IsOpen);
        var attempt = Assert.Single(fixture.Disruptions.Attempts);
        Assert.Equal(DisruptionOutcome.Rescheduled, attempt.Outcome);
        Assert.Equal(Actor, attempt.ContactedByUserId);
        var notification = Assert.Single(fixture.Publisher.Published);
        Assert.Equal(PatientNotificationKind.AppointmentRescheduledByClinic, notification.Kind);
    }

    [Fact]
    public async Task Reschedule_to_the_same_doctor_inside_blocked_time_is_refused()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(12, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var item = fixture.ItemFor(disruptionId, appointment.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RescheduleFromWorklistAsync(item.Id, Actor, fixture.Doctor.Id, Today, new TimeOnly(14, 0), null, CancellationToken.None));

        Assert.Equal("The appointment overlaps a doctor's break.", exception.Message);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.True(item.IsOpen);
        Assert.Empty(fixture.Publisher.Published);
    }

    [Fact]
    public async Task Cancel_from_worklist_uses_clinic_unavailable_closes_the_item_and_notifies()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(12, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var item = fixture.ItemFor(disruptionId, appointment.Id);

        await fixture.Service.CancelFromWorklistAsync(item.Id, Actor, "Patient unreachable", CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, appointment.CancellationReason);
        Assert.Equal(DisruptionOutcome.Cancelled, item.Outcome);
        var attempt = Assert.Single(fixture.Disruptions.Attempts);
        Assert.Equal("Patient unreachable", attempt.Notes);
        Assert.Single(fixture.Publisher.Published, notification => notification.Kind == PatientNotificationKind.AppointmentCancelledClinicUnavailable);
    }

    [Fact]
    public async Task Logging_contact_keeps_the_item_open_and_closed_items_refuse_further_actions()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(12, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var item = fixture.ItemFor(disruptionId, appointment.Id);

        await fixture.Service.LogContactAsync(item.Id, Actor, "Called, no answer", CancellationToken.None);

        Assert.Equal(DisruptionOutcome.Contacted, item.Outcome);
        Assert.True(item.IsOpen);
        Assert.Equal(Actor, item.LastContactedByUserId);
        Assert.Equal(DisruptionOutcome.Contacted, Assert.Single(fixture.Disruptions.Attempts).Outcome);

        await fixture.Service.CancelFromWorklistAsync(item.Id, Actor, null, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.LogContactAsync(item.Id, Actor, "Too late", CancellationToken.None));
        Assert.Contains("already resolved", exception.Message);
    }

    [Fact]
    public async Task Walk_in_items_cannot_be_rescheduled_from_the_worklist()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        fixture.WalkIn(1);
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var walkInItem = fixture.Disruptions.Items.Values.Single(item => item.DisruptionId == disruptionId && item.AppointmentId is null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RescheduleFromWorklistAsync(walkInItem.Id, Actor, fixture.Doctor.Id, Today, new TimeOnly(15, 0), null, CancellationToken.None));

        Assert.Contains("walk-in", exception.Message);
        Assert.True(walkInItem.IsOpen);
    }

    [Fact]
    public async Task Confirm_refuses_a_pending_request_inside_blocked_time_and_accepts_one_before_it()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var inside = fixture.Pending(new TimeOnly(14, 0));
        var before = fixture.Pending(new TimeOnly(10, 0));
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AppointmentService.ConfirmAsync(inside.Id, CancellationToken.None));
        Assert.Equal("The appointment overlaps a doctor's break.", exception.Message);
        Assert.Equal(AppointmentStatus.Pending, inside.Status);

        await fixture.AppointmentService.ConfirmAsync(before.Id, CancellationToken.None);
        Assert.Equal(AppointmentStatus.Confirmed, before.Status);
    }

    [Fact]
    public async Task Warning_provider_reports_only_unresolved_items_for_confirmed_appointments()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var affected = fixture.Confirmed(new TimeOnly(12, 0));
        var unaffected = fixture.Confirmed(new TimeOnly(9, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var provider = new DisruptionCheckInWarningProvider(fixture.Disruptions);

        var warning = await provider.GetWarningAsync(affected.Id, CancellationToken.None);
        Assert.NotNull(warning);
        Assert.Contains("left early from 11:00", warning);
        Assert.Contains("Emergency", warning);
        Assert.Null(await provider.GetWarningAsync(unaffected.Id, CancellationToken.None));

        await fixture.Service.CancelFromWorklistAsync(fixture.ItemFor(disruptionId, affected.Id).Id, Actor, null, CancellationToken.None);
        Assert.Null(await provider.GetWarningAsync(affected.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Check_in_succeeds_with_a_warning_and_never_marks_a_worklist_patient_no_show()
    {
        var fixture = new Fixture(At(11, 30));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(11, 0));
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(10, 45), "Emergency", Actor, CancellationToken.None);
        var checkIn = new CheckInService(fixture.Appointments, fixture.Queue, new FixedExpectedDuration(20), fixture.Disruptions,
            fixture.UnitOfWork, fixture.Clock, new NoopGracePeriodService(),
            new DoctorCallWindowService(fixture.Appointments, fixture.Disruptions, fixture.Schedule),
            new DisruptionCheckInWarningProvider(fixture.Disruptions));

        var result = await checkIn.CheckInScheduledPatientAsync(appointment.Id, fixture.Doctor.Id, CancellationToken.None);

        Assert.NotNull(result.QueueEntryId);
        Assert.False(result.RequiresWalkInRegistration);
        Assert.Contains("left early from 10:45", result.Warning);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Null(appointment.FrozenGracePeriodMinutes);
    }

    [Fact]
    public async Task Queue_board_warns_only_on_rows_still_waiting_for_the_doctor()
    {
        var fixture = new Fixture(At(10, 0));
        fixture.AddWindow(9, 17);
        var waiting = fixture.Confirmed(new TimeOnly(12, 0));
        fixture.CheckIn(waiting);
        var walkIn = fixture.WalkIn(1);
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(11, 0), "Emergency", Actor, CancellationToken.None);
        var board = new QueueBoardService(new InMemoryQueueBoardRepository(fixture.Appointments, fixture.Queue), fixture.Appointments,
            fixture.Approvals, new FixedExpectedDuration(20), fixture.Clock, fixture.Disruptions);

        var result = await board.GetDoctorQueueBoardAsync(fixture.Doctor.Id, Today, CancellationToken.None);

        var row = Assert.Single(result.Scheduled);
        Assert.Contains("left early from 11:00", row.DisruptionWarning);
        Assert.Contains("worklist", Assert.Single(result.WalkIns).DisruptionWarning);
        Assert.Equal(walkIn.Id, Assert.Single(result.WalkIns).QueueEntryId);
    }

    [Fact]
    public async Task Call_next_refuses_after_the_doctor_left_early_and_keeps_checked_in_patients_on_the_worklist()
    {
        var fixture = new Fixture(At(9, 50));
        fixture.AddWindow(9, 17);
        var due = fixture.Confirmed(new TimeOnly(10, 0));
        var dueEntry = fixture.CheckIn(due);
        var next = fixture.Confirmed(new TimeOnly(11, 0));
        var nextEntry = fixture.CheckIn(next);
        await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 30), "Emergency", Actor, CancellationToken.None);
        fixture.Clock.UtcNow = At(10, 30);
        var queueService = new ReceptionQueueService(fixture.Queue, fixture.Appointments, fixture.Approvals,
            new InMemoryQueueBoardRepository(fixture.Appointments, fixture.Queue), new NoopGracePeriodService(), fixture.UnitOfWork,
            fixture.Clock, fixture.Availability, fixture.Service,
            new DoctorCallWindowService(fixture.Appointments, fixture.Disruptions, fixture.Schedule));

        var result = await queueService.CallNextAsync(fixture.Doctor.Id, CancellationToken.None);

        Assert.False(result.Called);
        Assert.Contains("left at 09:30", result.Message);
        Assert.Contains("disruption worklist", result.Message);
        // The checked-in patient is never cancelled automatically; staff handle the worklist.
        Assert.Equal(AppointmentStatus.Confirmed, due.Status);
        Assert.Equal(QueueEntryStatus.Waiting, dueEntry.Status);
        Assert.Equal(QueueEntryStatus.Waiting, nextEntry.Status);
    }

    [Fact]
    public async Task Time_based_processing_never_cancels_a_checked_in_patient_but_day_end_still_cancels_them()
    {
        var fixture = new Fixture(At(10, 30));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(10, 0));
        var entry = fixture.CheckIn(appointment);
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(10, 15), "Emergency", Actor, CancellationToken.None);

        await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(QueueEntryStatus.Waiting, entry.Status);
        Assert.True(fixture.ItemFor(disruptionId, appointment.Id).IsOpen);

        await fixture.Service.RecordDayEndAsync(disruptionId, Actor, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, appointment.CancellationReason);
    }

    [Fact]
    public async Task Time_based_processing_still_cancels_a_due_patient_who_has_not_checked_in()
    {
        var fixture = new Fixture(At(9, 50));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(10, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 45), "Emergency", Actor, CancellationToken.None);
        fixture.Clock.UtcNow = At(10, 30);

        await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(AppointmentCancellationReasons.ClinicUnavailable, appointment.CancellationReason);
        Assert.False(fixture.ItemFor(disruptionId, appointment.Id).IsOpen);
    }

    [Fact]
    public async Task An_appointment_that_completed_closes_its_item_without_cancelling_or_changing_its_outcome()
    {
        var fixture = new Fixture(At(9, 50));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(10, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 45), "Emergency", Actor, CancellationToken.None);
        appointment.MarkCompleted(At(10, 10));
        fixture.Clock.UtcNow = At(10, 30);

        await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);

        var item = fixture.ItemFor(disruptionId, appointment.Id);
        Assert.False(item.IsOpen);
        Assert.Equal("Appointment completed.", item.ResolutionNote);
        Assert.Equal(DisruptionOutcome.Pending, item.Outcome);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Empty(fixture.Publisher.Published);
    }

    [Fact]
    public async Task An_appointment_rescheduled_elsewhere_closes_its_item_with_the_rescheduled_outcome()
    {
        var fixture = new Fixture(At(9, 50));
        fixture.AddWindow(9, 17);
        var appointment = fixture.Confirmed(new TimeOnly(10, 0));
        var disruptionId = await fixture.Service.RecordAsync(fixture.Doctor.Id, Today, new TimeOnly(9, 45), "Emergency", Actor, CancellationToken.None);
        appointment.MarkRescheduled(At(9, 55));
        fixture.Clock.UtcNow = At(10, 30);

        await fixture.Service.ProcessUnresolvedAsync(fixture.Clock.UtcNow, CancellationToken.None);

        var item = fixture.ItemFor(disruptionId, appointment.Id);
        Assert.False(item.IsOpen);
        Assert.Equal(DisruptionOutcome.Rescheduled, item.Outcome);
        Assert.Empty(await fixture.Disruptions.GetOpenItemsUpToDateAsync(Today, CancellationToken.None));
    }

    private static DateTimeOffset At(int hour, int minute) => new(Today.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset now)
        {
            Clock = new FixedClock(now, Today);
            Doctor = ScheduleTestData.NewDoctor();
            Appointments.AddDoctor(Doctor);
            Availability = new DoctorAvailabilityService(Appointments, Schedule, Clock);
            AppointmentService = new AppointmentService(Appointments, UnitOfWork, Clock, Queue, Availability);
            Disruptions = new InMemoryDoctorDisruptionRepository(Appointments, Queue, Schedule);
            Service = new DisruptionService(Disruptions, Appointments, Queue, Approvals, AppointmentService, Availability, Publisher, UnitOfWork, Clock);
        }

        public FixedClock Clock { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public InMemoryApprovalRepository Approvals { get; } = new();
        public InMemoryDoctorScheduleRepository Schedule { get; } = new();
        public InMemoryUnitOfWork UnitOfWork { get; } = new();
        public RecordingPatientNotificationPublisher Publisher { get; } = new();
        public InMemoryDoctorDisruptionRepository Disruptions { get; }
        public DoctorAvailabilityService Availability { get; }
        public AppointmentService AppointmentService { get; }
        public DisruptionService Service { get; }
        public Doctor Doctor { get; }

        public void AddWindow(int startHour, int endHour, Doctor? doctor = null) =>
            ScheduleTestData.AddWeeklyWindow(Appointments, (doctor ?? Doctor).Id, Today, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

        public Doctor AddDoctor(int startHour, int endHour)
        {
            var doctor = ScheduleTestData.NewDoctor();
            Appointments.AddDoctor(doctor);
            AddWindow(startHour, endHour, doctor);
            return doctor;
        }

        public Appointment Confirmed(TimeOnly start)
        {
            var appointment = Pending(start);
            appointment.Confirm(Clock.UtcNow);
            return appointment;
        }

        public Appointment Pending(TimeOnly start)
        {
            var appointment = Appointment.Request(Guid.NewGuid(), Doctor.Id, Today, start, start.AddMinutes(20), Clock.UtcNow);
            // The board and queue read these navigations, as EF would load them.
            QueueTestData.LinkAppointment(appointment, new Patient(appointment.PatientId, Guid.NewGuid(), "Test Patient", "+201000000111"));
            Appointments.Appointments[appointment.Id] = appointment;
            return appointment;
        }

        public QueueEntry CheckIn(Appointment appointment)
        {
            var entry = QueueEntry.CreateScheduled(appointment.PatientId, appointment.DoctorId, appointment.Id, Today, Clock.UtcNow);
            QueueTestData.Link(entry, appointment.Patient, appointment);
            Queue.Add(entry);
            return entry;
        }

        public QueueEntry WalkIn(long sequence)
        {
            var entry = QueueEntry.CreateWalkIn(Guid.NewGuid(), Doctor.Id, Today, Clock.UtcNow, sequence);
            QueueTestData.Link(entry, new Patient(entry.PatientId, Guid.NewGuid(), "Walk-in Patient", "+201000000222"), null);
            Queue.Add(entry);
            return entry;
        }

        public DisruptionWorklistItem ItemFor(Guid disruptionId, Guid appointmentId) =>
            Disruptions.Items.Values.Single(item => item.DisruptionId == disruptionId && item.AppointmentId == appointmentId);

        public GracePeriodService GracePeriods() =>
            new(Appointments, Queue, new FixedExpectedDuration(20), Disruptions, UnitOfWork, Clock);
    }
}
