using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Item 10: deactivating a doctor cancels future live appointments, cancels waiting entries, and notifies patients.</summary>
public sealed class DoctorDeactivationTests
{
    private static readonly DateOnly Today = QueueTestData.Today;

    [Fact]
    public async Task Deactivation_cancels_future_live_appointments_with_the_reason_and_keeps_those_already_started_today()
    {
        var w = new World(At(10, 0));
        var tomorrow = w.Confirmed(Today.AddDays(1), new TimeOnly(9, 0));
        var startedToday = w.Confirmed(Today, new TimeOnly(9, 0));
        var laterToday = w.Pending(Today, new TimeOnly(11, 0));

        var result = await w.Service.DeactivateAsync(w.DoctorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("2 appointment(s) cancelled and 2 patient(s) notified", result.Message);
        Assert.Equal(AppointmentStatus.Cancelled, tomorrow.Status);
        Assert.Equal(AppointmentCancellationReasons.DoctorNoLongerAvailable, tomorrow.CancellationReason);
        Assert.Equal(AppointmentStatus.Cancelled, laterToday.Status);
        Assert.Equal(AppointmentCancellationReasons.DoctorNoLongerAvailable, laterToday.CancellationReason);
        Assert.Equal(AppointmentStatus.Confirmed, startedToday.Status);
        Assert.False(w.Doctor.IsActive);
        Assert.Equal(2, w.Publisher.Published.Count);
        Assert.All(w.Publisher.Published, item => Assert.Equal(PatientNotificationKind.AppointmentCancelledDoctorNoLongerAvailable, item.Kind));
    }

    [Fact]
    public async Task Deactivation_cancels_waiting_walk_ins_and_withdraws_their_pending_approval_and_notifies_them()
    {
        var w = new World(At(10, 0));
        var walkIn = QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Today, At(9, 30), 1);
        w.Queue.Add(walkIn);
        var pending = WalkInAdmissionApproval.Request(walkIn, At(9, 31), At(10, 1));
        w.Approvals.Items[pending.Id] = pending;

        var result = await w.Service.DeactivateAsync(w.DoctorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(QueueEntryStatus.Cancelled, walkIn.Status);
        Assert.Equal(WalkInAdmissionDecision.Expired, pending.Decision);
        var notification = Assert.Single(w.Publisher.Published);
        Assert.Equal(PatientNotificationKind.WalkInCancelledDoctorNoLongerAvailable, notification.Kind);
        Assert.Equal(walkIn.PatientId, notification.PatientId);
    }

    [Fact]
    public async Task Deactivation_is_refused_while_a_patient_is_called_or_in_consultation_and_changes_nothing()
    {
        var w = new World(At(10, 0));
        var future = w.Confirmed(Today.AddDays(1), new TimeOnly(9, 0));
        var called = w.Confirmed(Today, new TimeOnly(11, 0));
        var entry = QueueEntry.CreateScheduled(called.PatientId, w.Doctor.Id, called.Id, Today, At(9, 50));
        entry.Call(At(10, 0));
        w.Queue.Add(entry);

        var result = await w.Service.DeactivateAsync(w.DoctorUserId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Called or in consultation", result.Message);
        Assert.True(w.Doctor.IsActive);
        Assert.Equal(AppointmentStatus.Confirmed, future.Status);
        Assert.Empty(w.Publisher.Published);
    }

    [Fact]
    public async Task Preview_reports_the_counts_that_deactivation_would_change_without_changing_anything()
    {
        var w = new World(At(10, 0));
        w.Confirmed(Today.AddDays(1), new TimeOnly(9, 0));
        w.Confirmed(Today, new TimeOnly(9, 0));
        w.Pending(Today, new TimeOnly(11, 0));
        w.Queue.Add(QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Today, At(9, 30), 1));

        var preview = await w.Service.PreviewAsync(w.DoctorUserId, CancellationToken.None);

        Assert.Equal(2, preview.AppointmentsToCancel);
        Assert.Equal(1, preview.WaitingEntriesToCancel);
        Assert.Null(preview.BlockReason);
        Assert.True(w.Doctor.IsActive);
        Assert.Empty(w.Publisher.Published);
    }

    [Fact]
    public async Task Preview_reports_the_block_when_a_patient_is_in_consultation()
    {
        var w = new World(At(10, 0));
        var walkIn = QueueEntry.CreateWalkIn(Guid.NewGuid(), w.Doctor.Id, Today, At(9, 30), 1);
        var approval = WalkInAdmissionApproval.Request(walkIn, At(9, 31), At(10, 1));
        approval.Approve(At(9, 32));
        walkIn.CallWalkIn(approval, At(9, 33), Today);
        walkIn.StartConsultation(At(9, 34));
        w.Queue.Add(walkIn);

        var preview = await w.Service.PreviewAsync(w.DoctorUserId, CancellationToken.None);

        Assert.NotNull(preview.BlockReason);
        Assert.Contains("Called or in consultation", preview.BlockReason);
    }

    private static DateTimeOffset At(int hour, int minute) => new(Today.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private sealed class World
    {
        public World(DateTimeOffset now)
        {
            Clock = new FixedClock(now, Today);
            Doctor = QueueTestData.NewDoctor(DoctorUserId);
            Appointments.AddDoctor(Doctor);
            Staff.Doctor = Doctor;
            var unitOfWork = new InMemoryUnitOfWork();
            var availability = new DoctorAvailabilityService(Appointments, new InMemoryDoctorScheduleRepository(), Clock);
            var appointmentService = new AppointmentService(Appointments, unitOfWork, Clock, Queue, availability);
            Service = new DoctorDeactivationService(Appointments, Queue, Approvals, Staff, appointmentService, Publisher, unitOfWork, Clock);
        }

        public Guid DoctorUserId { get; } = Guid.NewGuid();
        public FixedClock Clock { get; }
        public Doctor Doctor { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public InMemoryApprovalRepository Approvals { get; } = new();
        public RecordingPatientNotificationPublisher Publisher { get; } = new();
        public DeactivationStaff Staff { get; } = new();
        public DoctorDeactivationService Service { get; }

        public Appointment Confirmed(DateOnly date, TimeOnly start)
        {
            var appointment = Appointment.Request(Guid.NewGuid(), Doctor.Id, date, start, start.AddMinutes(20), Clock.UtcNow);
            appointment.Confirm(Clock.UtcNow);
            Appointments.Appointments[appointment.Id] = appointment;
            return appointment;
        }

        public Appointment Pending(DateOnly date, TimeOnly start)
        {
            var appointment = Appointment.Request(Guid.NewGuid(), Doctor.Id, date, start, start.AddMinutes(20), Clock.UtcNow);
            Appointments.Appointments[appointment.Id] = appointment;
            return appointment;
        }
    }

    /// <summary>Account flag store that also flips the doctor profile, as the EF repository does.</summary>
    private sealed class DeactivationStaff : IStaffAccountRepository
    {
        public Doctor? Doctor { get; set; }

        public Task<IReadOnlyList<StaffAccountRow>> ListStaffAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StaffAccountRow>>([]);

        public Task<string?> GetRoleNameAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("Doctor");

        public Task<bool> SetAccountActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
        {
            if (Doctor is not null)
            {
                if (isActive) Doctor.Activate(); else Doctor.Deactivate();
            }
            return Task.FromResult(true);
        }
    }
}
