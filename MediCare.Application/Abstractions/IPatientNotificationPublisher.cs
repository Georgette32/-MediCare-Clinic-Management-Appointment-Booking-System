using MediCare.Application.Enums;

namespace MediCare.Application.Abstractions;

/// <summary>
/// Hand-off point for patient notifications. Called after the state change has committed. The implementation must log
/// and swallow delivery failures: a notification problem never fails the business operation that triggered it.
/// </summary>
public interface IPatientNotificationPublisher
{
    Task PublishAsync(PatientNotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// A patient notification. DedupKey, when set, makes delivery idempotent per recipient (for example "reminder:{appointmentId}").
/// </summary>
public sealed record PatientNotification(
    Guid PatientId,
    PatientNotificationKind Kind,
    string Title,
    string Message,
    Guid? RelatedAppointmentId,
    string? DedupKey = null);
