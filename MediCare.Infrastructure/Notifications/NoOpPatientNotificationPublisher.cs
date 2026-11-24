using MediCare.Application.Abstractions;

namespace MediCare.Infrastructure.Notifications;

/// <summary>
/// Placeholder until Phase 3 selects the notification store and provider. Publishing is intentionally a no-op so
/// that the state changes that trigger notifications are already in place.
/// </summary>
public sealed class NoOpPatientNotificationPublisher : IPatientNotificationPublisher
{
    public Task PublishAsync(PatientNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
