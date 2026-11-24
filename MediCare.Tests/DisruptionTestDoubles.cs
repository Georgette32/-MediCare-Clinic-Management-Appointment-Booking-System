using System.Reflection;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;

namespace MediCare.Tests;

/// <summary>
/// In-memory disruption store. Navigation properties that EF would load are resolved on read from the appointment and
/// queue fakes (when supplied), so the same tracked instances are mutated, as with the EF change tracker.
/// </summary>
internal sealed class InMemoryDoctorDisruptionRepository(
    InMemoryAppointmentRepository? appointments = null,
    InMemoryQueueRepository? queue = null,
    InMemoryDoctorScheduleRepository? schedule = null) : IDoctorDisruptionRepository
{
    public Dictionary<Guid, DoctorDisruption> Disruptions { get; } = [];
    public List<DoctorBreak> Breaks { get; } = [];
    public Dictionary<Guid, DisruptionWorklistItem> Items { get; } = [];
    public List<DisruptionContactAttempt> Attempts { get; } = [];
    public Dictionary<Guid, Patient> Patients { get; } = [];
    public Dictionary<Guid, string> UserNames { get; } = [];

    public Task<DoctorDisruption?> GetByIdAsync(Guid disruptionId, CancellationToken cancellationToken) =>
        Task.FromResult(Disruptions.TryGetValue(disruptionId, out var disruption) ? WithDoctor(disruption) : null);

    public Task<bool> ExistsForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Disruptions.Values.Any(item => item.DoctorId == doctorId && item.Date == date));

    public Task<DoctorDisruption?> GetForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Disruptions.Values.SingleOrDefault(item => item.DoctorId == doctorId && item.Date == date));

    public Task<IReadOnlyList<DoctorDisruption>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DoctorDisruption>>(Disruptions.Values
            .OrderByDescending(item => item.Date)
            .Take(take)
            .Select(WithDoctor)
            .ToArray());

    public Task AddDisruptionAsync(DoctorDisruption disruption, CancellationToken cancellationToken)
    {
        Disruptions[disruption.Id] = disruption;
        return Task.CompletedTask;
    }

    public Task AddBreaksAsync(IReadOnlyList<DoctorBreak> breaks, CancellationToken cancellationToken)
    {
        // The break check (appointments) and availability (schedule) read separate fakes; both must see the block, as the database does.
        Breaks.AddRange(breaks);
        appointments?.Breaks.AddRange(breaks);
        schedule?.Breaks.AddRange(breaks);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DisruptionWorklistItem>> GetItemsForDisruptionAsync(Guid disruptionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DisruptionWorklistItem>>(Items.Values
            .Where(item => item.DisruptionId == disruptionId)
            .Select(Resolve)
            .ToArray());

    public Task<DisruptionWorklistItem?> GetItemByIdAsync(Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.TryGetValue(itemId, out var item) ? Resolve(item) : null);

    public Task AddItemsAsync(IReadOnlyList<DisruptionWorklistItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
            Items[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task AddContactAttemptAsync(DisruptionContactAttempt attempt, CancellationToken cancellationToken)
    {
        Attempts.Add(attempt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DisruptionContactAttempt>> GetAttemptsForDisruptionAsync(Guid disruptionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DisruptionContactAttempt>>(Attempts
            .Where(attempt => Items.TryGetValue(attempt.WorklistItemId, out var item) && item.DisruptionId == disruptionId)
            .OrderBy(attempt => attempt.ContactedAtUtc)
            .ToArray());

    public Task<IReadOnlyList<DisruptionWorklistItem>> GetOpenItemsUpToDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DisruptionWorklistItem>>(Items.Values
            .Where(item => item.IsOpen && Disruptions.TryGetValue(item.DisruptionId, out var disruption) && disruption.Date <= date)
            .Select(Resolve)
            .ToArray());

    public Task<DisruptionWorklistItem?> GetOpenItemForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Values
            .Where(item => item.AppointmentId == appointmentId && item.IsOpen)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(Resolve)
            .FirstOrDefault());

    public Task<IReadOnlyList<DisruptionWorklistItem>> GetOpenItemsForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DisruptionWorklistItem>>(Items.Values
            .Where(item => item.IsOpen &&
                           Disruptions.TryGetValue(item.DisruptionId, out var disruption) &&
                           disruption.DoctorId == doctorId && disruption.Date == date)
            .Select(Resolve)
            .ToArray());

    public Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds
            .Where(UserNames.ContainsKey)
            .ToDictionary(id => id, id => UserNames[id]));

    private DoctorDisruption WithDoctor(DoctorDisruption disruption)
    {
        SetNavigation(disruption, nameof(DoctorDisruption.Doctor), appointments?.Doctors.GetValueOrDefault(disruption.DoctorId));
        return disruption;
    }

    private DisruptionWorklistItem Resolve(DisruptionWorklistItem item)
    {
        SetNavigation(item, nameof(DisruptionWorklistItem.Disruption),
            Disruptions.TryGetValue(item.DisruptionId, out var disruption) ? WithDoctor(disruption) : null);
        SetNavigation(item, nameof(DisruptionWorklistItem.Patient), PatientFor(item.PatientId));
        SetNavigation(item, nameof(DisruptionWorklistItem.Appointment),
            item.AppointmentId is Guid appointmentId ? appointments?.Appointments.GetValueOrDefault(appointmentId) : null);
        SetNavigation(item, nameof(DisruptionWorklistItem.QueueEntry),
            item.QueueEntryId is Guid entryId ? queue?.Entries.GetValueOrDefault(entryId) : null);
        return item;
    }

    private Patient PatientFor(Guid patientId)
    {
        if (!Patients.TryGetValue(patientId, out var patient))
        {
            patient = new Patient(patientId, Guid.NewGuid(), "Test Patient", "+201000000" + Random.Shared.Next(100, 999));
            Patients[patientId] = patient;
        }
        return patient;
    }

    private static void SetNavigation(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(target, value);
}

internal sealed class RecordingPatientNotificationPublisher : IPatientNotificationPublisher
{
    public List<PatientNotification> Published { get; } = [];

    public Task PublishAsync(PatientNotification notification, CancellationToken cancellationToken)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>For tests that exercise other services; disruption processing is a no-op there.</summary>
internal sealed class NoopDisruptionService : IDisruptionService
{
    public Task<Guid> RecordAsync(Guid doctorId, DateOnly date, TimeOnly fromTime, string reason, Guid actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DisruptionListItem>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<DisruptionWorklist> GetWorklistAsync(Guid disruptionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task LogContactAsync(Guid itemId, Guid actorUserId, string? notes, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Guid> RescheduleFromWorklistAsync(Guid itemId, Guid actorUserId, Guid newDoctorId, DateOnly newDate, TimeOnly newStartTime, string? notes, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task CancelFromWorklistAsync(Guid itemId, Guid actorUserId, string? notes, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RecordDayEndAsync(Guid disruptionId, Guid actorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> ProcessUnresolvedAsync(DateTimeOffset clinicNowUtc, CancellationToken cancellationToken) => Task.FromResult(0);
}
