using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Notifications;

/// <summary>
/// The in-app provider: writes a Notification row. A concurrent insert of the same DedupKey is reported as "already
/// delivered" (false) instead of an error. The unsaved entity is detached so later saves in the same scope stay clean.
/// </summary>
public sealed class InAppNotificationProvider(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : INotificationProvider
{
    public async Task<bool> DeliverAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(
            delivery.RecipientUserId,
            delivery.Kind,
            delivery.Title,
            delivery.Message,
            delivery.RelatedEntityType,
            delivery.RelatedEntityId,
            delivery.DedupKey,
            clinicClock.UtcNow);

        await notifications.AddAsync(notification, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            notifications.Detach(notification);
            return false;
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
                return true;
        }
        return false;
    }
}
