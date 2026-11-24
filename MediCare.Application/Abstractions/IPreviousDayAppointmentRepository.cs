using MediCare.Domain.Enums;

namespace MediCare.Application.Abstractions;

/// <summary>Read model used by the previous-day appointment resolver.</summary>
public interface IPreviousDayAppointmentRepository
{
    /// <summary>Ids of Confirmed appointments dated strictly before the date, oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetConfirmedBeforeDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Statuses of every attendance attempt (queue entry) linked to the appointment. Empty when the patient never checked in.</summary>
    Task<IReadOnlyList<QueueEntryStatus>> GetAttemptStatusesAsync(Guid appointmentId, CancellationToken cancellationToken);
}
