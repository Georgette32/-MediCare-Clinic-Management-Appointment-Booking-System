using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using Microsoft.Extensions.Logging;

namespace MediCare.Infrastructure.Notifications;

/// <summary>
/// Patient publisher used after a committed business change. Delivery failures are logged and swallowed: a notification
/// problem must never fail the operation that already succeeded. Cancellation is still propagated.
/// </summary>
public sealed class SafePatientNotificationPublisher(
    INotificationService notifications,
    ILogger<SafePatientNotificationPublisher> logger) : IPatientNotificationPublisher
{
    public async Task PublishAsync(PatientNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.NotifyPatientAsync(
                notification.PatientId,
                new NotificationDraft(
                    notification.Kind.ToString(),
                    notification.Title,
                    notification.Message,
                    notification.RelatedAppointmentId is null ? null : "Appointment",
                    notification.RelatedAppointmentId,
                    notification.DedupKey),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Patient notification {Kind} for patient {PatientId} could not be delivered.", notification.Kind, notification.PatientId);
        }
    }
}

/// <summary>Staff publisher used after a committed business change. Logs and swallows delivery failures.</summary>
public sealed class SafeStaffNotificationPublisher(
    INotificationService notifications,
    ILogger<SafeStaffNotificationPublisher> logger) : IStaffNotificationPublisher
{
    public async Task PublishToDoctorAsync(Guid doctorId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.NotifyDoctorAsync(doctorId, new NotificationDraft(kind.ToString(), title, message, relatedEntityType, relatedEntityId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Staff notification {Kind} for doctor {DoctorId} could not be delivered.", kind, doctorId);
        }
    }

    public async Task PublishToUserAsync(Guid userId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.NotifyUserAsync(userId, new NotificationDraft(kind.ToString(), title, message, relatedEntityType, relatedEntityId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Staff notification {Kind} for user {UserId} could not be delivered.", kind, userId);
        }
    }
}
