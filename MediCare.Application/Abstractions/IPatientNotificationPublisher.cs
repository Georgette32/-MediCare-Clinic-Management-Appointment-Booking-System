using MediCare.Application.Enums;

namespace MediCare.Application.Abstractions;

/// <summary>
/// Hand-off point for patient notifications. Phase 3 replaces the no-op implementation with in-app notification
/// records and, later, a delivery provider. Called after the state change has committed.
/// </summary>
public interface IPatientNotificationPublisher
{
    Task PublishAsync(PatientNotification notification, CancellationToken cancellationToken);
}

public sealed record PatientNotification(
    Guid PatientId,
    PatientNotificationKind Kind,
    string Title,
    string Message,
    Guid? RelatedAppointmentId);
