using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IDoctorDisruptionRepository
{
    /// <summary>Tracked disruption with its doctor loaded.</summary>
    Task<DoctorDisruption?> GetByIdAsync(Guid disruptionId, CancellationToken cancellationToken);
    Task<bool> ExistsForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    /// <summary>The doctor's disruption on the date (at most one), or null. Read-only.</summary>
    Task<DoctorDisruption?> GetForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoctorDisruption>> GetRecentAsync(int take, CancellationToken cancellationToken);
    Task AddDisruptionAsync(DoctorDisruption disruption, CancellationToken cancellationToken);
    Task AddBreaksAsync(IReadOnlyList<DoctorBreak> breaks, CancellationToken cancellationToken);

    /// <summary>Tracked worklist items of the disruption with patient, appointment, queue entry and disruption loaded.</summary>
    Task<IReadOnlyList<DisruptionWorklistItem>> GetItemsForDisruptionAsync(Guid disruptionId, CancellationToken cancellationToken);
    /// <summary>Tracked worklist item with its patient, appointment, queue entry and disruption loaded.</summary>
    Task<DisruptionWorklistItem?> GetItemByIdAsync(Guid itemId, CancellationToken cancellationToken);
    Task AddItemsAsync(IReadOnlyList<DisruptionWorklistItem> items, CancellationToken cancellationToken);
    Task AddContactAttemptAsync(DisruptionContactAttempt attempt, CancellationToken cancellationToken);
    /// <summary>History for every item of the disruption, oldest first.</summary>
    Task<IReadOnlyList<DisruptionContactAttempt>> GetAttemptsForDisruptionAsync(Guid disruptionId, CancellationToken cancellationToken);

    /// <summary>
    /// Open items whose disruption date is on or before the date. Used by the clinic-unavailable processor, which
    /// decides per item from the appointment and queue state at operation time.
    /// </summary>
    Task<IReadOnlyList<DisruptionWorklistItem>> GetOpenItemsUpToDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The open item for the appointment, or null. Used by Grace Period, No-Show and check-in warnings.</summary>
    Task<DisruptionWorklistItem?> GetOpenItemForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);

    /// <summary>Open items of one doctor's disruptions on the date (queue board warnings).</summary>
    Task<IReadOnlyList<DisruptionWorklistItem>> GetOpenItemsForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Display names of Identity users (staff who acted). Missing ids are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
