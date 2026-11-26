using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Fixed UTC instant for SystemClinicClock tests.</summary>
internal sealed class FlowTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

/// <summary>Clinic clock with settable date and time for the booking rules.</summary>
internal sealed class FlowClock(DateTimeOffset utcNow, DateOnly today) : IClinicClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
    public DateOnly Today { get; set; } = today;
    public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
    public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
        new(DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc));
}

internal sealed class FlowUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }
    public int SaveCount { get; private set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
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

    private sealed class Transaction(FlowUnitOfWork owner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            owner.CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Appointment store for the patient and clinic flows. AddAsync also records the slot in the schedule double so
/// availability (alternatives) sees booked time, as the database does.
/// </summary>
internal sealed class FlowAppointmentRepository(InMemoryDoctorScheduleRepository schedule) : IAppointmentRepository
{
    public HashSet<Guid> PatientIds { get; } = [];
    public Dictionary<Guid, Doctor> Doctors { get; } = [];
    public List<DoctorWorkingHour> WorkingHours { get; } = [];
    public Dictionary<Guid, Appointment> Items { get; } = [];

    public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(PatientIds.Contains(patientId));
    public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Values.Any(item => item.PatientId == patientId && item.DoctorId == doctorId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed));
    public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken) => Task.FromResult(Doctors.GetValueOrDefault(doctorId));
    public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Doctors.Values.SingleOrDefault(doctor => doctor.ApplicationUserId == applicationUserId));
    public Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorWorkingHour>>(WorkingHours.Where(item => item.DoctorId == doctorId && item.DayOfWeek == dayOfWeek).ToArray());
    public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) =>
        Task.FromResult(schedule.Breaks.Any(item => item.DoctorId == doctorId && item.Date == date && item.StartTime < end && item.EndTime > start));

    public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Values.Any(item =>
            item.Id != exceptAppointmentId && item.DoctorId == doctorId && item.AppointmentDate == date &&
            item.StartTime < end && item.EndTime > start &&
            item.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed));

    public Task AddAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        Items.Add(appointment.Id, appointment);
        schedule.Booked.Add((appointment.DoctorId, appointment.AppointmentDate, appointment.StartTime, appointment.EndTime));
        return Task.CompletedTask;
    }

    public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken) => Task.FromResult(Items.GetValueOrDefault(appointmentId));

    public Task<IReadOnlyList<Appointment>> GetLiveForDoctorFromDateAsync(Guid doctorId, DateOnly fromDate, CancellationToken ct) => Task.FromResult<IReadOnlyList<Appointment>>([]);
    public Task<bool> HasLivePatientOverlapAsync(Guid patientId, DateOnly date, TimeOnly start, TimeOnly end, IReadOnlyCollection<Guid> excludedAppointmentIds, CancellationToken ct) => Task.FromResult(false);
    public Task<Appointment?> GetLiveReplacementAsync(Guid originalAppointmentId, CancellationToken ct) => Task.FromResult<Appointment?>(null);
    public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Values.Any(item => item.RescheduledFromAppointmentId == appointmentId && item.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed));

    public Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Appointment>>(Items.Values.Where(item => item.Status == AppointmentStatus.Pending).ToArray());

    public Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Appointment>>(Items.Values
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed)
            .ToArray());
}

/// <summary>Read-model double: records the last search and returns the rows it was given.</summary>
internal sealed class FlowQueryRepository : IAppointmentQueryRepository
{
    public List<AppointmentRecord> Appointments { get; } = [];
    public List<PatientSearchRecord> Patients { get; } = [];
    public List<LiveReplacementRecord> Replacements { get; } = [];
    public (string? ExactPhone, string? Digits, string? Name)? LastSearch { get; set; }

    public Task<Guid?> GetPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Patients.Where(item => item.ApplicationUserId == applicationUserId).Select(item => (Guid?)item.PatientId).SingleOrDefault());

    public Task<PatientSearchRecord?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult(Patients.SingleOrDefault(item => item.PatientId == patientId));

    public Task<IReadOnlyList<PatientSearchRecord>> SearchPatientsAsync(string? exactPhone, string? phoneDigits, string? nameContains, CancellationToken cancellationToken)
    {
        LastSearch = (exactPhone, phoneDigits, nameContains);
        return Task.FromResult<IReadOnlyList<PatientSearchRecord>>([]);
    }

    public Task<IReadOnlyList<AppointmentRecord>> GetForPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AppointmentRecord>>(Appointments.Where(item => item.PatientId == patientId).ToArray());

    public Task<IReadOnlyList<AppointmentRecord>> GetForDateAsync(DateOnly date, Guid? doctorId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AppointmentRecord>>(Appointments
            .Where(item => item.Date == date && (doctorId is null || item.DoctorId == doctorId)).ToArray());

    public Task<AppointmentRecord?> GetRecordAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Appointments.SingleOrDefault(item => item.Id == appointmentId));

    public Task<IReadOnlyList<LiveReplacementRecord>> GetLiveReplacementsAsync(Guid[] originalAppointmentIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveReplacementRecord>>(Replacements.Where(item => originalAppointmentIds.Contains(item.OriginalAppointmentId)).ToArray());
}

internal sealed class FlowIdentityProfileStore : IIdentityProfileStore
{
    public Dictionary<Guid, IdentityProfileSnapshot> Users { get; } = [];
    public int UpdateCount { get; private set; }
    public string? UniqueEmailViolationFor { get; set; }

    public Task<IdentityProfileSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.GetValueOrDefault(userId));

    public Task<IdentityProfileOperationResult> SetFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken)
    {
        UpdateCount++;
        var user = Users[userId];
        Users[userId] = user with { FullName = fullName };
        return Task.FromResult(IdentityProfileOperationResult.Success());
    }

    public Task<IdentityProfileOperationResult> SetEmailAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        UpdateCount++;
        if (UniqueEmailViolationFor is not null && email == UniqueEmailViolationFor)
            return Task.FromResult(IdentityProfileOperationResult.Failure("An account with this email already exists."));
        var user = Users[userId];
        Users[userId] = user with { Email = email };
        return Task.FromResult(IdentityProfileOperationResult.Success());
    }
}

internal sealed class FlowProfileRepository : IAccountProfileRepository
{
    public List<Patient> Patients { get; } = [];
    public List<Doctor> Doctors { get; } = [];

    public Task<Patient?> GetPatientByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Patients.SingleOrDefault(item => item.ApplicationUserId == applicationUserId));

    public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Doctors.SingleOrDefault(item => item.ApplicationUserId == applicationUserId));
}

/// <summary>Only EmailExistsAsync is exercised by the profile service; account creation is not used here.</summary>
internal sealed class FlowIdentityAccounts(params string[] takenEmails) : IIdentityAccountStore
{
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(takenEmails.Contains(email, StringComparer.OrdinalIgnoreCase));

    public Task<IdentityAccountCreateResult> CreateAsync(string userName, string? email, string fullName, string? phoneNumber, string role, string password, bool mustChangePassword, bool requireEmailVerification, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IdentityAccountSnapshot?> FindAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IdentityPasswordResetResult> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
