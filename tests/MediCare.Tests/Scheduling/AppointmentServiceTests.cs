using System.Data;
using MediCare.Application.DTOs.Analytics;
using MediCare.Application.DTOs.Scheduling;
using MediCare.Application.Enums;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Tests.Scheduling;

public sealed class AppointmentServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly AppointmentDate = new(2026, 12, 1);

    [Fact]
    public async Task Booking_uses_doctor_duration_and_stores_pending_request()
    {
        var fixture = CreateFixture();

        var appointmentId = await fixture.Service.RequestAsync(fixture.PatientId, fixture.DoctorId, AppointmentDate, new TimeOnly(9, 20), CancellationToken.None);

        var appointment = fixture.Repository.Items[appointmentId];
        Assert.Equal(new TimeOnly(9, 40), appointment.EndTime);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Null(fixture.Repository.LastExcludedAppointmentId);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Patient_cancellation_is_denied_on_appointment_day_and_allowed_before()
    {
        var fixture = CreateFixture();
        var appointment = AddConfirmedAppointment(fixture);

        fixture.Clock.Today = AppointmentDate;
        await Assert.ThrowsAsync<DomainException>(() => fixture.Service.CancelByPatientAsync(appointment.Id, fixture.PatientId, null, CancellationToken.None));
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);

        fixture.Clock.Today = AppointmentDate.AddDays(-1);
        await fixture.Service.CancelByPatientAsync(appointment.Id, fixture.PatientId, "Patient request", CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("Patient request", appointment.CancellationReason);
    }

    [Fact]
    public async Task Reception_and_admin_may_cancel_on_appointment_day_with_reason()
    {
        foreach (var actor in new[] { ClinicCancellationActor.Reception, ClinicCancellationActor.Admin })
        {
            var fixture = CreateFixture();
            fixture.Clock.Today = AppointmentDate;
            var appointment = AddConfirmedAppointment(fixture);

            await fixture.Service.CancelByClinicAsync(appointment.Id, actor, "Doctor unavailable", CancellationToken.None);

            Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
            Assert.Equal("Doctor unavailable", appointment.CancellationReason);
        }
    }

    [Fact]
    public async Task Clinic_cancellation_rejects_non_clinic_actor()
    {
        var fixture = CreateFixture();
        var appointment = AddConfirmedAppointment(fixture);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CancelByClinicAsync(appointment.Id, (ClinicCancellationActor)99, "Reason", CancellationToken.None));

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
    }

    [Fact]
    public async Task Doctor_completion_marks_appointment_completed_and_saves_notes()
    {
        var fixture = CreateFixture();
        var appointment = AddConfirmedAppointment(fixture);
        var doctorUserId = fixture.Repository.Doctors[fixture.DoctorId].UserId;

        await fixture.Service.CompleteByDoctorAsync(
            appointment.Id, doctorUserId, "  Review notes  ", CancellationToken.None);

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal("Review notes", appointment.DoctorNotes);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Doctor_completion_rejects_another_doctors_appointment()
    {
        var fixture = CreateFixture();
        var appointment = AddConfirmedAppointment(fixture);
        var otherDoctorId = AddDoctor(fixture, "Dr. Other");
        var otherUserId = fixture.Repository.Doctors[otherDoctorId].UserId;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CompleteByDoctorAsync(
                appointment.Id, otherUserId, null, CancellationToken.None));

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Patient_reschedule_is_denied_on_appointment_day_and_allowed_before()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);

        fixture.Clock.Today = AppointmentDate;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(1), new TimeOnly(9, 0), CancellationToken.None));
        Assert.Equal(AppointmentStatus.Confirmed, original.Status);

        fixture.Clock.Today = AppointmentDate.AddDays(-1);
        var replacementId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate, new TimeOnly(9, 20), CancellationToken.None);
        var replacement = fixture.Repository.Items[replacementId];

        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
        Assert.Equal(AppointmentStatus.Pending, replacement.Status);
        Assert.Equal(original.Id, replacement.RescheduledFromAppointmentId);
        Assert.Equal(original.Id, fixture.Repository.LastExcludedAppointmentId);
    }

    [Fact]
    public async Task Patient_reschedule_rejects_same_doctor_date_and_start_time()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate, new TimeOnly(9, 0), CancellationToken.None));

        Assert.Contains("different doctor, appointment date, or start time", error.Message);
        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
        Assert.Single(fixture.Repository.Items);
    }

    [Fact]
    public async Task Patient_reschedule_accepts_different_date_or_start_and_keeps_original_confirmed()
    {
        foreach (var (date, start) in new[]
        {
            (AppointmentDate.AddDays(1), new TimeOnly(9, 0)),
            (AppointmentDate, new TimeOnly(9, 20))
        })
        {
            var fixture = CreateFixture();
            var original = AddConfirmedAppointment(fixture);

            var replacementId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, date, start, CancellationToken.None);

            Assert.Equal(AppointmentStatus.Confirmed, original.Status);
            Assert.Equal(AppointmentStatus.Pending, fixture.Repository.Items[replacementId].Status);
        }
    }

    [Fact]
    public async Task Patient_reschedule_accepts_different_doctor_and_keeps_original_confirmed()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);
        var replacementDoctorId = AddDoctor(fixture, "Dr. Jones");

        var replacementId = await fixture.Service.RescheduleAsync(
            original.Id, fixture.PatientId, replacementDoctorId, AppointmentDate, new TimeOnly(9, 0), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
        Assert.Equal(replacementDoctorId, fixture.Repository.Items[replacementId].DoctorProfileId);
        Assert.Equal(AppointmentStatus.Pending, fixture.Repository.Items[replacementId].Status);
    }

    [Fact]
    public async Task Confirming_linked_replacement_atomically_swaps_appointments()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);
        var replacementId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(1), new TimeOnly(9, 0), CancellationToken.None);
        var replacement = fixture.Repository.Items[replacementId];
        var commitsBeforeConfirmation = fixture.UnitOfWork.CommitCount;

        await fixture.Service.ConfirmAsync(replacementId, CancellationToken.None);

        Assert.Equal(AppointmentStatus.Confirmed, replacement.Status);
        Assert.Equal(AppointmentStatus.Rescheduled, original.Status);
        Assert.Equal(commitsBeforeConfirmation + 1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Rejected_replacement_leaves_original_unchanged_and_allows_new_attempt()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);
        var rejectedId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(1), new TimeOnly(9, 0), CancellationToken.None);

        await fixture.Service.RejectAsync(rejectedId, CancellationToken.None);
        var nextId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(2), new TimeOnly(9, 0), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
        Assert.Equal(AppointmentStatus.Rejected, fixture.Repository.Items[rejectedId].Status);
        Assert.Equal(AppointmentStatus.Pending, fixture.Repository.Items[nextId].Status);
    }

    [Fact]
    public async Task Cancelled_replacement_leaves_original_unchanged_and_allows_new_attempt()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);
        var cancelledId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(1), new TimeOnly(9, 0), CancellationToken.None);

        await fixture.Service.CancelByPatientAsync(cancelledId, fixture.PatientId, null, CancellationToken.None);
        var nextId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(2), new TimeOnly(9, 0), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
        Assert.Equal(AppointmentStatus.Cancelled, fixture.Repository.Items[cancelledId].Status);
        Assert.Equal(AppointmentStatus.Pending, fixture.Repository.Items[nextId].Status);
    }

    [Fact]
    public async Task Patient_reschedule_uses_linked_original_exclusion_for_conflict_check()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);

        var replacementId = await fixture.Service.RescheduleAsync(original.Id, fixture.PatientId, fixture.DoctorId, AppointmentDate.AddDays(1), new TimeOnly(9, 0), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, replacementId);
        Assert.Equal(original.Id, fixture.Repository.LastExcludedAppointmentId);
        Assert.Equal(AppointmentStatus.Confirmed, original.Status);
    }

    [Fact]
    public async Task Clinic_replacement_is_confirmed_and_original_rescheduled_in_one_transaction()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);
        var commitsBefore = fixture.UnitOfWork.CommitCount;

        var replacementId = await fixture.Service.RescheduleByClinicAsync(
            original.Id, ClinicCancellationActor.Reception, fixture.DoctorId, AppointmentDate.AddDays(1), new TimeOnly(9, 0), CancellationToken.None);

        Assert.Equal(AppointmentStatus.Rescheduled, original.Status);
        Assert.Equal(AppointmentStatus.Confirmed, fixture.Repository.Items[replacementId].Status);
        Assert.Equal(commitsBefore + 1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Reschedule_request_can_be_cancelled_with_reason_when_no_slot_is_found()
    {
        var fixture = CreateFixture();
        var original = AddConfirmedAppointment(fixture);

        await fixture.Service.CancelByClinicAsync(original.Id, ClinicCancellationActor.Admin, "No replacement slot available", CancellationToken.None);

        Assert.Equal(AppointmentStatus.Cancelled, original.Status);
        Assert.Equal("No replacement slot available", original.CancellationReason);
    }

    private static Appointment AddConfirmedAppointment(Fixture fixture)
    {
        var appointment = Appointment.Request(fixture.PatientId, fixture.DoctorId, AppointmentDate, new TimeOnly(9, 0), new TimeOnly(9, 20), Now);
        appointment.Confirm(Now);
        fixture.Repository.Items.Add(appointment.Id, appointment);
        return appointment;
    }

    private static Fixture CreateFixture()
    {
        var repository = new FakeAppointmentRepository();
        var patientId = new PatientProfile(Guid.NewGuid()).Id;
        repository.PatientIds.Add(patientId);
        var doctorId = AddDoctor(repository, "Dr. Smith");
        var unitOfWork = new FakeUnitOfWork();
        var clock = new FakeClinicClock(Now, AppointmentDate.AddDays(-1));
        return new Fixture(new AppointmentService(repository, unitOfWork, clock), repository, unitOfWork, clock, patientId, doctorId);
    }

    private static Guid AddDoctor(Fixture fixture, string name) => AddDoctor(fixture.Repository, name);

    private static Guid AddDoctor(FakeAppointmentRepository repository, string name)
    {
        var doctorId = Guid.NewGuid();
        var doctor = new DoctorProfile(Guid.NewGuid(), Guid.NewGuid(), "LIC-" + doctorId.ToString("N"), 100m, 5, null, 20);
        repository.Doctors.Add(doctor.Id, doctor);
        AddSchedules(repository, doctor.Id);
        return doctor.Id;
    }

    private static void AddSchedules(FakeAppointmentRepository repository, Guid doctorId)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
            repository.Schedules.Add(DoctorWorkingHour.Create(doctorId, day, new TimeOnly(9, 0), new TimeOnly(11, 0)));
    }

    private sealed record Fixture(
        AppointmentService Service,
        FakeAppointmentRepository Repository,
        FakeUnitOfWork UnitOfWork,
        FakeClinicClock Clock,
        Guid PatientId,
        Guid DoctorId);

    private sealed class FakeClinicClock(DateTime utcNow, DateOnly today) : IClinicClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public DateOnly Today { get; set; } = today;
        public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow);
        public DateTime ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
            DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public int CommitCount { get; private set; }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
            Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(this));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(SaveCount);
        }

        public async Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            await using var transaction = await BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await operation(cancellationToken);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            ExecuteSerializableWithRetryAsync(operation, cancellationToken);

        public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            ExecuteSerializableWithRetryAsync(operation, cancellationToken);

        private sealed class FakeTransaction(FakeUnitOfWork owner) : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                owner.CommitCount++;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        public HashSet<Guid> PatientIds { get; } = [];
        public Dictionary<Guid, DoctorProfile> Doctors { get; } = [];
        public List<DoctorWorkingHour> Schedules { get; } = [];
        public Dictionary<Guid, Appointment> Items { get; } = [];
        public Guid? LastExcludedAppointmentId { get; private set; }

        public Task<int> CountAsync(AppointmentStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CountUpcomingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CountByDateRangeAsync(DateTime startDateUtc, DateTime endDateUtc, AppointmentStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<PeakHoursDto>> GetPeakHoursAsync(DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PeakHoursDto>>([]);

        public Task<IReadOnlyList<SpecializationAnalyticsDto>> GetPopularSpecializationsAsync(DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpecializationAnalyticsDto>>([]);

        public Task<bool> PatientExistsAsync(Guid patientProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(PatientIds.Contains(patientProfileId));

        public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientProfileId, Guid doctorProfileId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Any(item => item.PatientProfileId == patientProfileId && item.DoctorProfileId == doctorProfileId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed));

        public Task<DoctorProfile?> GetDoctorProfileAsync(Guid doctorProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(Doctors.GetValueOrDefault(doctorProfileId));

        public Task<DoctorProfile?> GetDoctorProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Doctors.Values.SingleOrDefault(doctor => doctor.UserId == userId));

        public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorProfileId, DayOfWeek dayOfWeek, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DoctorWorkingHour>>(Schedules.Where(item => item.DoctorProfileId == doctorProfileId && item.DayOfWeek == dayOfWeek).ToArray());

        public Task<bool> IsHolidayAsync(Guid doctorProfileId, DateOnly date, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> HasBreakOverlapAsync(Guid doctorProfileId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> HasAppointmentConflictAsync(Guid doctorProfileId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken cancellationToken)
        {
            LastExcludedAppointmentId = exceptAppointmentId;
            var conflict = Items.Values.Any(item =>
                item.Id != exceptAppointmentId && item.DoctorProfileId == doctorProfileId && item.AppointmentDate == date &&
                item.StartTime < end && item.EndTime > start &&
                item.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed);
            return Task.FromResult(conflict);
        }

        public Task AddAsync(Appointment appointment, CancellationToken cancellationToken)
        {
            Items.Add(appointment.Id, appointment);
            return Task.CompletedTask;
        }

        public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(appointmentId));

        public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Any(item => item.RescheduledFromAppointmentId == appointmentId && item.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed));

        public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Appointment>>(Items.Values.Where(item => item.Status == AppointmentStatus.Pending).ToArray());

        public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorProfileId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Appointment>>(Items.Values
                .Where(item => item.DoctorProfileId == doctorProfileId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed)
                .OrderBy(item => item.StartTime)
                .ToArray());

        public Task<IReadOnlyList<PendingAppointmentDto>> GetPendingWithNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PendingAppointmentDto>>([]);

        public Task<string?> GetPatientNameAsync(Guid patientProfileId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
