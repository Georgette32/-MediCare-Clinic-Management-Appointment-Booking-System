using System.Data;
using System.Reflection;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>
/// In-memory doubles for the queue read/write paths. They honour the same live-attempt, FIFO and scheduled-order
/// rules as the EF queries. Clinic time equals UTC in these tests.
/// </summary>
internal static class QueueTestData
{
    public static readonly DateOnly Today = new(2026, 12, 1);
    public static readonly DateTimeOffset Noon = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    public static Patient NewPatient(string name) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, "+201000000" + Random.Shared.Next(100, 999));

    public static Doctor NewDoctor(Guid? userId = null) =>
        new(Guid.NewGuid(), userId ?? Guid.NewGuid(), "Dr. Test", Guid.NewGuid(), 20);

    /// <summary>Sets the private navigation properties EF would normally populate on load.</summary>
    public static void Link(QueueEntry entry, Patient patient, Appointment? appointment = null)
    {
        SetNavigation(entry, nameof(QueueEntry.Patient), patient);
        SetNavigation(entry, nameof(QueueEntry.Appointment), appointment);
    }

    public static void LinkAppointment(Appointment appointment, Patient patient) =>
        SetNavigation(appointment, nameof(Appointment.Patient), patient);

    private static void SetNavigation(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
}

internal sealed class FixedClock(DateTimeOffset utcNow, DateOnly today) : IClinicClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
    public DateOnly Today { get; set; } = today;
    public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
    public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
        new(DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc));
}

internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        RunAsync(operation, cancellationToken);

    public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        RunAsync(operation, cancellationToken);

    public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        RunAsync(operation, cancellationToken);

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private sealed class Transaction(InMemoryUnitOfWork owner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            owner.CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class InMemoryQueueRepository : IQueueRepository
{
    public Dictionary<Guid, QueueEntry> Entries { get; } = [];

    public void Add(QueueEntry entry) => Entries[entry.Id] = entry;

    private IEnumerable<QueueEntry> Live => Entries.Values.Where(entry => entry.Status.IsLive());

    public Task<bool> HasLiveQueueEntryForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Live.Any(entry => entry.AppointmentId == appointmentId));

    public Task<bool> HasActiveEntryForPatientAsync(Guid patientId, Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        Task.FromResult(Live.Any(entry => entry.PatientId == patientId && entry.DoctorId == doctorId && entry.QueueDate == queueDate));

    public Task<long> GetNextWalkInSequenceAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        Task.FromResult(Entries.Values
            .Where(entry => entry.DoctorId == doctorId && entry.QueueDate == queueDate && entry.QueueSequence is not null)
            .Select(entry => entry.QueueSequence!.Value)
            .DefaultIfEmpty(0L)
            .Max() + 1);

    public Task AddAsync(QueueEntry queueEntry, CancellationToken cancellationToken)
    {
        Add(queueEntry);
        return Task.CompletedTask;
    }

    public Task<QueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Entries.GetValueOrDefault(id));

    public Task<QueueEntry?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var attempts = Entries.Values.Where(entry => entry.AppointmentId == appointmentId).ToArray();
        return Task.FromResult(attempts.FirstOrDefault(entry => entry.Status.IsLive())
            ?? attempts.OrderByDescending(entry => entry.ArrivedAt).FirstOrDefault());
    }

    public Task<IReadOnlyList<QueueEntry>> GetActiveEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QueueEntry>>(Live
            .Where(entry => entry.DoctorId == doctorId && entry.QueueDate == queueDate)
            .OrderBy(entry => entry.ArrivedAt)
            .ThenBy(entry => entry.QueueSequence)
            .ToArray());

    public Task<IReadOnlyList<QueueEntry>> GetScheduledEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QueueEntry>>(Live
            .Where(entry => entry.DoctorId == doctorId && entry.QueueDate == queueDate && entry.Source == QueueEntrySource.Scheduled)
            .OrderBy(entry => entry.Appointment?.StartTime)
            .ToArray());

    public Task<IReadOnlyList<QueueEntry>> GetWalkInEntriesForDoctorAndDateAsync(Guid doctorId, DateOnly queueDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QueueEntry>>(Live
            .Where(entry => entry.DoctorId == doctorId && entry.QueueDate == queueDate && entry.Source == QueueEntrySource.WalkIn)
            .OrderBy(entry => entry.ArrivedAt)
            .ThenBy(entry => entry.QueueSequence)
            .ToArray());

    public Task<IReadOnlyList<QueueEntry>> GetLiveEntriesBeforeDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QueueEntry>>(Entries.Values.Where(item => item.QueueDate < date && item.Status.IsLive()).ToArray());

    public Task<IReadOnlyList<QueueEntry>> GetCompletedVisitsAsync(Guid doctorId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QueueEntry>>([]);
}

internal sealed class InMemoryAppointmentRepository : IAppointmentRepository
{
    public Dictionary<Guid, Doctor> Doctors { get; } = [];
    public Dictionary<Guid, Appointment> Appointments { get; } = [];
    public List<DoctorWorkingHour> WorkingHours { get; } = [];
    public List<DoctorHoliday> Holidays { get; } = [];
    public List<DoctorBreak> Breaks { get; } = [];

    public void AddDoctor(Doctor doctor) => Doctors[doctor.Id] = doctor;

    public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Appointments.Values.Any(item => item.PatientId == patientId && item.DoctorId == doctorId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed));
    public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken) =>
        Task.FromResult(Doctors.TryGetValue(doctorId, out var doctor) && doctor.IsActive ? doctor : null);
    public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Doctors.Values.SingleOrDefault(doctor => doctor.ApplicationUserId == applicationUserId && doctor.IsActive));
    public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHour>>(WorkingHours.Where(item => item.DoctorId == doctorId && item.DayOfWeek == dayOfWeek).ToArray());
    public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Holidays.Any(item => item.DoctorId == doctorId && item.StartDate <= date && item.EndDate >= date));
    public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) =>
        Task.FromResult(Breaks.Any(item => item.DoctorId == doctorId && item.Date == date && item.StartTime < end && item.EndTime > start));
    public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Appointments.Values.Any(item =>
            item.Id != exceptAppointmentId && item.DoctorId == doctorId && item.AppointmentDate == date &&
            item.StartTime < end && item.EndTime > start &&
            item.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed));
    public Task AddAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        Appointments[appointment.Id] = appointment;
        return Task.CompletedTask;
    }
    public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Appointments.GetValueOrDefault(appointmentId));
    public Task<IReadOnlyList<Appointment>> GetLiveForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Appointment>>(Appointments.Values
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate >= fromDate && AppointmentPolicy.IsLive(item.Status))
            .OrderBy(item => item.AppointmentDate).ThenBy(item => item.StartTime).ToArray());
    public Task<bool> HasLivePatientOverlapAsync(Guid patientId, DateOnly date, TimeOnly start, TimeOnly end, IReadOnlyCollection<Guid> excludedAppointmentIds, CancellationToken ct) =>
        Task.FromResult(Appointments.Values.Any(item => item.PatientId == patientId && item.AppointmentDate == date &&
            item.StartTime < end && item.EndTime > start && AppointmentPolicy.IsLive(item.Status) && !excludedAppointmentIds.Contains(item.Id)));
    public Task<Appointment?> GetLiveReplacementAsync(Guid originalAppointmentId, CancellationToken ct) =>
        Task.FromResult(Appointments.Values.SingleOrDefault(item => item.RescheduledFromAppointmentId == originalAppointmentId && AppointmentPolicy.IsLive(item.Status)));
    public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Appointments.Values.Any(item => item.RescheduledFromAppointmentId == appointmentId && AppointmentPolicy.IsLive(item.Status)));
    public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Appointment>>([]);
    public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Appointment>>(Appointments.Values
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed)
            .OrderBy(item => item.StartTime)
            .ToArray());
}

internal sealed class InMemoryApprovalRepository : IWalkInAdmissionApprovalRepository
{
    public Dictionary<Guid, WalkInAdmissionApproval> Items { get; } = [];

    public Task<bool> HasPendingForQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Values.Any(item => item.QueueEntryId == queueEntryId && item.Decision == WalkInAdmissionDecision.Pending));
    public Task AddAsync(WalkInAdmissionApproval approval, CancellationToken cancellationToken)
    {
        Items[approval.Id] = approval;
        return Task.CompletedTask;
    }
    public Task<WalkInAdmissionApproval?> GetByIdAsync(Guid approvalId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.GetValueOrDefault(approvalId));
    public Task<IReadOnlyList<WalkInAdmissionApproval>> GetPendingForDoctorAsync(Guid doctorId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WalkInAdmissionApproval>>(Items.Values
            .Where(item => item.DoctorId == doctorId && item.Decision == WalkInAdmissionDecision.Pending)
            .ToArray());
    public Task<IReadOnlyList<WalkInAdmissionApproval>> GetForQueueEntriesAsync(IReadOnlyCollection<Guid> queueEntryIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WalkInAdmissionApproval>>(Items.Values
            .Where(item => queueEntryIds.Contains(item.QueueEntryId))
            .OrderBy(item => item.RequestedAtUtc)
            .ToArray());
}

internal sealed class InMemoryQueueBoardRepository(InMemoryAppointmentRepository appointments, InMemoryQueueRepository queue) : IQueueBoardRepository
{
    public Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Appointment>>(appointments.Appointments.Values
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate == date)
            .OrderBy(item => item.StartTime)
            .ToArray());

    public Task<IReadOnlyList<QueueEntry>> GetDoctorQueueEntriesForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QueueEntry>>(queue.Entries.Values
            .Where(item => item.DoctorId == doctorId && item.QueueDate == date)
            .ToArray());

    public Task<IReadOnlyList<Doctor>> GetActiveDoctorsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Doctor>>(appointments.Doctors.Values.Where(doctor => doctor.IsActive).ToArray());

    public Task<Guid?> FindPatientIdByPhoneAsync(string canonicalPhone, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);
}

/// <summary>Grace processing is covered by its own tests; here it is a no-op so queue decisions can be isolated.</summary>
internal sealed class NoopGracePeriodService : IGracePeriodService
{
    public Task<GracePeriodStartResult> StartGracePeriodIfEligibleAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(new GracePeriodStartResult(appointmentId, 0, DateTimeOffset.MinValue, StartedSuccessfully: false));

    public Task<int> ProcessExpiredGracePeriodsForDoctorAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<int> ProcessDoctorDayAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

internal sealed class FixedExpectedDuration(int minutes = 20) : IExpectedDurationService
{
    public Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
        Task.FromResult(new ExpectedDurationResult(minutes, UsedHistoricalAverage: false, ValidSampleCount: 0, RawAverageMinutes: null));
}

/// <summary>A historical-average result with a known spread and sample, for the range and long-consultation rules.</summary>
internal sealed class SampleExpectedDuration(int minutes, double? stdDev, double[] sample) : IExpectedDurationService
{
    public Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken) =>
        Task.FromResult(new ExpectedDurationResult(minutes, UsedHistoricalAverage: true, ValidSampleCount: sample.Length,
            RawAverageMinutes: sample.Average(), StdDevMinutes: stdDev, SampleMinutes: sample));
}
