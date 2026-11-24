using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>
/// Business-review fixes in AppointmentService: no overlapping live appointments per patient (item 9), patient cancellation
/// and replacement rules (item 8), and the friendly conflict message for unique-index races (item 7).
/// </summary>
public sealed class AppointmentReviewFixTests
{
    private static readonly DateOnly Today = QueueTestData.Today;
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    [Fact]
    public async Task Request_refuses_a_slot_that_overlaps_another_live_appointment_of_the_same_patient_with_any_doctor()
    {
        var w = new World();
        w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), new TimeOnly(10, 45));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.RequestAsync(w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(10, 20), CancellationToken.None));

        Assert.Equal("The patient already has an appointment at this time.", exception.Message);
    }

    [Fact]
    public async Task Request_allows_back_to_back_and_cancelled_overlaps()
    {
        var w = new World();
        w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), new TimeOnly(10, 20));
        var cancelled = w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(11, 0), new TimeOnly(11, 45));
        cancelled.CancelByClinic("test", w.Clock.UtcNow);

        var backToBack = await w.Service.RequestAsync(w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(10, 20), CancellationToken.None);
        var overCancelled = await w.Service.RequestAsync(w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(11, 20), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, backToBack);
        Assert.NotEqual(Guid.Empty, overCancelled);
    }

    [Fact]
    public async Task Clinic_booking_refuses_an_overlapping_patient_appointment()
    {
        var w = new World();
        w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), new TimeOnly(10, 45));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.BookByClinicAsync(w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(10, 20), ClinicCancellationActor.Reception, CancellationToken.None));

        Assert.Equal("The patient already has an appointment at this time.", exception.Message);
    }

    [Fact]
    public async Task Reschedule_refuses_a_replacement_that_overlaps_another_live_appointment_of_the_patient()
    {
        var w = new World();
        var original = w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(9, 0), new TimeOnly(9, 20));
        w.Insert(w.PatientId, w.ThirdDoctor.Id, Tomorrow, new TimeOnly(11, 0), new TimeOnly(11, 45));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.RescheduleAsync(original.Id, w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(11, 20), CancellationToken.None));

        Assert.Equal("The patient already has an appointment at this time.", exception.Message);
    }

    [Fact]
    public async Task Replacement_may_overlap_its_own_original_and_confirms_when_nothing_else_overlaps()
    {
        var w = new World();
        var original = w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(9, 0), new TimeOnly(9, 45));
        var replacementId = await w.Service.RescheduleAsync(original.Id, w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(9, 20), CancellationToken.None);

        await w.Service.ConfirmAsync(replacementId, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Rescheduled, original.Status);
        Assert.Equal(AppointmentStatus.Confirmed, w.Appointments.Appointments[replacementId].Status);
    }

    [Fact]
    public async Task Confirm_refuses_a_replacement_that_now_overlaps_another_appointment_of_the_patient()
    {
        var w = new World();
        var original = w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(9, 0), new TimeOnly(9, 45));
        var replacementId = await w.Service.RescheduleAsync(original.Id, w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(9, 20), CancellationToken.None);
        w.Insert(w.PatientId, w.ThirdDoctor.Id, Tomorrow, new TimeOnly(9, 30), new TimeOnly(9, 50));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.ConfirmAsync(replacementId, CancellationToken.None));

        Assert.Equal("The patient already has an appointment at this time.", exception.Message);
        Assert.Equal(AppointmentStatus.Pending, w.Appointments.Appointments[replacementId].Status);
        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
    }

    [Fact]
    public async Task Patient_cancelling_the_original_also_cancels_its_pending_replacement()
    {
        var w = new World();
        var original = w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), new TimeOnly(10, 20));
        var replacementId = await w.Service.RescheduleAsync(original.Id, w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(12, 0), CancellationToken.None);

        await w.Service.CancelByPatientAsync(original.Id, w.PatientId, "changed my mind", CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, original.Status);
        var replacement = w.Appointments.Appointments[replacementId];
        Assert.Equal(AppointmentStatus.Cancelled, replacement.Status);
        Assert.Equal(AppointmentCancellationReasons.OriginalCancelled, replacement.CancellationReason);
    }

    [Fact]
    public async Task Patient_replacement_cannot_be_confirmed_once_the_original_day_has_arrived()
    {
        var w = new World();
        var original = w.Insert(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), new TimeOnly(10, 20));
        var replacementId = await w.Service.RescheduleAsync(original.Id, w.PatientId, w.OtherDoctor.Id, Tomorrow, new TimeOnly(12, 0), CancellationToken.None);
        w.Clock.Today = Tomorrow;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.ConfirmAsync(replacementId, CancellationToken.None));

        Assert.Equal("The original appointment's day has arrived; reject this reschedule request.", exception.Message);
        Assert.Equal(AppointmentStatus.Pending, w.Appointments.Appointments[replacementId].Status);
        Assert.Equal(AppointmentStatus.Confirmed, original.Status);

        await w.Service.RejectAsync(replacementId, CancellationToken.None);
        Assert.Equal(AppointmentStatus.Rejected, w.Appointments.Appointments[replacementId].Status);
    }

    [Fact]
    public async Task Clinic_cancellation_is_refused_for_an_appointment_dated_before_clinic_today()
    {
        var w = new World();
        var past = w.Insert(w.PatientId, w.Doctor.Id, Today.AddDays(-1), new TimeOnly(10, 0), new TimeOnly(10, 20));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.CancelByClinicAsync(past.Id, ClinicCancellationActor.Reception, "clinic closed", CancellationToken.None));

        Assert.Equal("Clinic cancellation and rescheduling apply only to today's and future appointments.", exception.Message);
        Assert.Equal(AppointmentStatus.Confirmed, past.Status);
    }

    [Fact]
    public async Task Clinic_reschedule_is_refused_for_an_appointment_dated_before_clinic_today()
    {
        var w = new World();
        var past = w.Insert(w.PatientId, w.Doctor.Id, Today.AddDays(-1), new TimeOnly(10, 0), new TimeOnly(10, 20));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.RescheduleByClinicAsync(past.Id, ClinicCancellationActor.Admin, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), CancellationToken.None));

        Assert.Equal("Clinic cancellation and rescheduling apply only to today's and future appointments.", exception.Message);
        Assert.Equal(AppointmentStatus.Confirmed, past.Status);
    }

    [Fact]
    public async Task A_unique_index_race_surfaces_as_the_friendly_slot_message_not_an_error()
    {
        var w = new World();
        w.UnitOfWork.FailWith(new UniqueConstraintConflictException("IX_Appointments_DoctorId_AppointmentDate_StartTime",
            "The requested appointment time is already reserved."));

        var exception = await Assert.ThrowsAsync<UniqueConstraintConflictException>(() =>
            w.Service.RequestAsync(w.PatientId, w.Doctor.Id, Tomorrow, new TimeOnly(10, 0), CancellationToken.None));

        Assert.Equal("The requested appointment time is already reserved.", exception.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task Starting_a_consultation_that_loses_the_one_active_consultation_race_returns_a_friendly_failure()
    {
        var w = new World();
        var userId = Guid.NewGuid();
        var doctor = QueueTestData.NewDoctor(userId);
        w.Appointments.AddDoctor(doctor);
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), doctor.Id, Guid.NewGuid(), Today, QueueTestData.Noon.AddMinutes(-10));
        entry.Call(QueueTestData.Noon.AddMinutes(-5));
        w.Queue.Add(entry);
        w.UnitOfWork.FailWith(new UniqueConstraintConflictException("IX_QueueEntries_DoctorId",
            "Another consultation is already in progress for this doctor. Complete it first."));
        var consultations = new ConsultationService(w.Queue, w.Appointments, w.UnitOfWork, w.Clock);

        var result = await consultations.StartConsultationAsync(entry.Id, userId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Another consultation is already in progress for this doctor. Complete it first.", result.Message);
    }

    private sealed class World
    {
        public World()
        {
            Doctor = QueueTestData.NewDoctor();
            OtherDoctor = QueueTestData.NewDoctor();
            ThirdDoctor = QueueTestData.NewDoctor();
            Appointments.AddDoctor(Doctor);
            Appointments.AddDoctor(OtherDoctor);
            Appointments.AddDoctor(ThirdDoctor);
            foreach (var doctor in new[] { Doctor, OtherDoctor, ThirdDoctor })
            foreach (var day in Enum.GetValues<DayOfWeek>())
                Appointments.WorkingHours.Add(DoctorWorkingHour.Create(doctor.Id, day, new TimeOnly(8, 0), new TimeOnly(17, 0)));
            var availability = new DoctorAvailabilityService(Appointments, new InMemoryDoctorScheduleRepository(), Clock);
            Service = new AppointmentService(Appointments, UnitOfWork, Clock, Queue, availability);
        }

        public FixedClock Clock { get; } = new(QueueTestData.Noon, Today);
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public FailingUnitOfWork UnitOfWork { get; } = new();
        public AppointmentService Service { get; }
        public Doctor Doctor { get; }
        public Doctor OtherDoctor { get; }
        public Doctor ThirdDoctor { get; }
        public Guid PatientId { get; } = Guid.NewGuid();

        /// <summary>Inserts a Confirmed appointment directly (bypassing booking rules) so tests can set up overlaps.</summary>
        public Appointment Insert(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end)
        {
            var appointment = Appointment.Request(patientId, doctorId, date, start, end, Clock.UtcNow);
            appointment.Confirm(Clock.UtcNow);
            Appointments.Appointments[appointment.Id] = appointment;
            return appointment;
        }
    }

    /// <summary>Runs operations normally, or throws the configured exception from the serializable wrapper.</summary>
    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        private Exception? failure;

        public void FailWith(Exception exception) => failure = exception;

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(System.Data.IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
            Task.FromResult<IUnitOfWorkTransaction>(new Transaction());
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            if (failure is not null)
                throw failure;
            return operation(cancellationToken);
        }
        public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            if (failure is not null)
                throw failure;
            return operation(cancellationToken);
        }

        private sealed class Transaction : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
