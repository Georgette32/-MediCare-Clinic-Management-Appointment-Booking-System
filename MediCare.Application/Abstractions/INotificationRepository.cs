using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface INotificationRepository
{
    /// <summary>The Identity user linked to the patient profile, or null when the patient does not exist.</summary>
    Task<Guid?> GetPatientUserIdAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>The Identity user linked to the doctor profile, or null when the doctor does not exist.</summary>
    Task<Guid?> GetDoctorUserIdAsync(Guid doctorId, CancellationToken cancellationToken);

    /// <summary>The Identity users of every active Receptionist account.</summary>
    Task<IReadOnlyList<Guid>> GetActiveReceptionUserIdsAsync(CancellationToken cancellationToken);

    Task<bool> ExistsForDedupKeyAsync(Guid recipientUserId, string dedupKey, CancellationToken cancellationToken);

    Task AddAsync(Notification notification, CancellationToken cancellationToken);

    /// <summary>Stops tracking a notification whose insert failed, so it is not retried by later saves.</summary>
    void Detach(Notification notification);

    /// <summary>One page of the user's notifications, newest first, with the total count.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked notification addressed to exactly this user, or null. Other users' rows are never returned.</summary>
    Task<Notification?> GetForRecipientAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Notification>> GetUnreadForUserAsync(Guid userId, CancellationToken cancellationToken);
}
