using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class ConsultationServiceTests
{
    [Fact]
    public async Task Start_moves_a_called_patient_into_consultation_for_the_owning_doctor()
    {
        var s = new QueueScenario();
        var entry = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("A"), new TimeOnly(9, 0)));
        entry.Call(s.Clock.UtcNow);

        var result = await s.Consultations.StartConsultationAsync(entry.Id, s.DoctorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(QueueEntryStatus.InConsultation, entry.Status);
    }

    [Fact]
    public async Task Start_is_refused_for_a_different_doctor_without_changing_state()
    {
        var s = new QueueScenario();
        var entry = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("A"), new TimeOnly(9, 0)));
        entry.Call(s.Clock.UtcNow);

        var result = await s.Consultations.StartConsultationAsync(entry.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(QueueEntryStatus.Called, entry.Status);
    }

    [Fact]
    public async Task Start_is_refused_while_another_consultation_is_in_progress()
    {
        var s = new QueueScenario();
        var first = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("First"), new TimeOnly(9, 0)));
        var second = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Second"), new TimeOnly(9, 30)));
        first.Call(s.Clock.UtcNow);
        first.StartConsultation(s.Clock.UtcNow);
        second.Call(s.Clock.UtcNow);

        var result = await s.Consultations.StartConsultationAsync(second.Id, s.DoctorUserId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already in progress", result.Message);
        Assert.Equal(QueueEntryStatus.Called, second.Status);
    }

    [Fact]
    public async Task Complete_marks_the_scheduled_appointment_completed_in_the_same_operation()
    {
        var s = new QueueScenario();
        var appointment = s.AddConfirmedAppointment(s.AddPatient("A"), new TimeOnly(9, 0));
        var entry = s.CheckIn(appointment);
        entry.Call(s.Clock.UtcNow);
        entry.StartConsultation(s.Clock.UtcNow);
        s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(18);

        var result = await s.Consultations.CompleteConsultationAsync(entry.Id, s.DoctorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(QueueEntryStatus.Completed, entry.Status);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.True(s.UnitOfWork.CommitCount >= 1);
    }

    [Fact]
    public async Task Complete_for_a_walk_in_leaves_no_appointment_to_update()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.CallWalkIn(walkIn);
        walkIn.StartConsultation(s.Clock.UtcNow);
        s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(10);

        var result = await s.Consultations.CompleteConsultationAsync(walkIn.Id, s.DoctorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(QueueEntryStatus.Completed, walkIn.Status);
        Assert.Null(walkIn.AppointmentId);
    }

    [Fact]
    public async Task Complete_is_refused_for_a_different_doctor()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);
        s.CallWalkIn(walkIn);
        walkIn.StartConsultation(s.Clock.UtcNow);
        s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(10);

        var result = await s.Consultations.CompleteConsultationAsync(walkIn.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(QueueEntryStatus.InConsultation, walkIn.Status);
    }

    [Fact]
    public async Task Complete_changes_nothing_when_the_appointment_is_no_longer_confirmed()
    {
        var s = new QueueScenario();
        var appointment = s.AddConfirmedAppointment(s.AddPatient("A"), new TimeOnly(9, 0));
        var entry = s.CheckIn(appointment);
        entry.Call(s.Clock.UtcNow);
        entry.StartConsultation(s.Clock.UtcNow);
        appointment.CancelByClinic("clinic unavailable", s.Clock.UtcNow);
        s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(10);

        var result = await s.Consultations.CompleteConsultationAsync(entry.Id, s.DoctorUserId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(QueueEntryStatus.InConsultation, entry.Status);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
    }

    [Fact]
    public async Task An_account_without_a_doctor_profile_gets_a_failure_result()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);

        var result = await s.Consultations.StartConsultationAsync(walkIn.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not linked", result.Message);
    }
}

public sealed class QueueBoardServiceTests
{
    [Fact]
    public async Task Estimates_start_after_the_called_patient_and_follow_call_next_order()
    {
        var s = new QueueScenario();
        var calledNow = s.AddConfirmedAppointment(s.AddPatient("Called"), new TimeOnly(8, 30));
        s.CheckIn(calledNow).Call(s.Clock.UtcNow);
        var scheduledWaiting = s.CheckIn(s.AddConfirmedAppointment(s.AddPatient("Scheduled"), new TimeOnly(9, 0)));
        var walkIn = s.AddWalkIn(s.AddPatient("Walk"), s.Clock.UtcNow.AddMinutes(-5), 1);

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        var now = s.Clock.UtcNow;
        var scheduledItem = board.Scheduled.Single(item => item.QueueEntryId == scheduledWaiting.Id);
        var walkInItem = board.WalkIns.Single(item => item.QueueEntryId == walkIn.Id);
        Assert.Equal(now.AddMinutes(20), scheduledItem.EstimatedStartUtc);
        Assert.Equal(now.AddMinutes(40), walkInItem.EstimatedStartUtc);
        Assert.Equal(QueueEntryStatus.Called, board.Active!.Status);
    }

    [Fact]
    public async Task Estimates_wait_for_a_running_consultation_but_never_start_before_now()
    {
        var s = new QueueScenario();
        var noon = s.Clock.UtcNow;
        var first = s.AddWalkIn(s.AddPatient("First"), noon.AddMinutes(-40), 1);
        // Called at 08:50 and started at 08:55, so the consultation runs until 09:15 at the expected 20 minutes.
        s.Clock.UtcNow = noon.AddMinutes(-10);
        s.CallWalkIn(first);
        first.StartConsultation(noon.AddMinutes(-5));
        s.Clock.UtcNow = noon;
        var next = s.AddWalkIn(s.AddPatient("Next"), noon.AddMinutes(-20), 2);
        s.Approve(next);

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        // Consultation started 5 minutes ago with 20 expected: it should end 15 minutes from now.
        var item = board.WalkIns.Single(walk => walk.QueueEntryId == next.Id);
        Assert.Equal(s.Clock.UtcNow.AddMinutes(15), item.EstimatedStartUtc);
    }

    [Fact]
    public async Task Waiting_patient_is_estimated_at_now_when_nobody_is_active()
    {
        var s = new QueueScenario();
        var walkIn = s.AddWalkIn(s.AddPatient("Only"), s.Clock.UtcNow.AddMinutes(-5), 1);

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        Assert.Null(board.Active);
        Assert.Equal(s.Clock.UtcNow, board.WalkIns.Single(item => item.QueueEntryId == walkIn.Id).EstimatedStartUtc);
    }

    [Fact]
    public async Task Scheduled_states_show_grace_running_expired_and_not_arrived()
    {
        var s = new QueueScenario();
        var frozen = s.AddConfirmedAppointment(s.AddPatient("Frozen"), new TimeOnly(8, 45));
        frozen.StartGracePeriod(60, s.Clock.UtcNow.AddMinutes(-15));
        var expired = s.AddConfirmedAppointment(s.AddPatient("Expired"), new TimeOnly(8, 0));
        var notYet = s.AddConfirmedAppointment(s.AddPatient("Later"), new TimeOnly(9, 30));

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        var grace = board.Scheduled.Single(item => item.AppointmentId == frozen.Id);
        Assert.Equal(ScheduledBoardState.GraceRunning, grace.State);
        Assert.Equal(s.Clock.UtcNow.AddMinutes(45), grace.GraceEndsAtUtc);
        Assert.Equal(ScheduledBoardState.GraceExpired, board.Scheduled.Single(item => item.AppointmentId == expired.Id).State);
        Assert.Equal(ScheduledBoardState.NotArrived, board.Scheduled.Single(item => item.AppointmentId == notYet.Id).State);
    }

    [Fact]
    public async Task A_patient_who_left_shows_not_arrived_with_a_note_and_can_check_in_again()
    {
        var s = new QueueScenario();
        var appointment = s.AddConfirmedAppointment(s.AddPatient("Left"), new TimeOnly(9, 30));
        s.CheckIn(appointment).Cancel();

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        var item = board.Scheduled.Single();
        Assert.Equal(ScheduledBoardState.NotArrived, item.State);
        Assert.True(item.PreviouslyLeftQueue);
        Assert.Null(item.QueueEntryId);
    }

    [Fact]
    public async Task Walk_in_approval_states_are_derived_from_the_latest_and_usable_records()
    {
        var s = new QueueScenario();
        var none = s.AddWalkIn(s.AddPatient("None"), s.Clock.UtcNow.AddMinutes(-40), 1);
        var rejected = s.AddWalkIn(s.AddPatient("Rejected"), s.Clock.UtcNow.AddMinutes(-35), 2);
        var rejectedRequest = WalkInAdmissionApproval.Request(rejected, s.Clock.UtcNow.AddMinutes(-3), s.Clock.UtcNow.AddMinutes(27));
        rejectedRequest.Reject(s.Clock.UtcNow.AddMinutes(-2));
        s.Approvals.Items[rejectedRequest.Id] = rejectedRequest;
        var approved = s.AddWalkIn(s.AddPatient("Approved"), s.Clock.UtcNow.AddMinutes(-30), 3);
        var approval = s.Approve(approved);
        var pending = s.AddWalkIn(s.AddPatient("Pending"), s.Clock.UtcNow.AddMinutes(-25), 4);
        var pendingRequest = WalkInAdmissionApproval.Request(pending, s.Clock.UtcNow.AddMinutes(-1), s.Clock.UtcNow.AddMinutes(29));
        s.Approvals.Items[pendingRequest.Id] = pendingRequest;

        var board = await s.BoardService.GetDoctorQueueBoardAsync(s.Doctor.Id, QueueTestData.Today, CancellationToken.None);

        Assert.Equal(WalkInApprovalState.None, board.WalkIns.Single(item => item.QueueEntryId == none.Id).ApprovalState);
        Assert.Equal(WalkInApprovalState.Rejected, board.WalkIns.Single(item => item.QueueEntryId == rejected.Id).ApprovalState);
        var approvedItem = board.WalkIns.Single(item => item.QueueEntryId == approved.Id);
        Assert.Equal(WalkInApprovalState.Approved, approvedItem.ApprovalState);
        Assert.Equal(approval.Id, approvedItem.UsableApprovalId);
        Assert.Equal(WalkInApprovalState.Pending, board.WalkIns.Single(item => item.QueueEntryId == pending.Id).ApprovalState);
    }
}
