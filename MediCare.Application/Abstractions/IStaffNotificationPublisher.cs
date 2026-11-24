using MediCare.Application.Enums;

namespace MediCare.Application.Abstractions;

/// <summary>
/// Hand-off point for clinician and staff notifications. Called after the business change has committed; the
/// implementation logs and swallows delivery failures.
/// </summary>
public interface IStaffNotificationPublisher
{
    /// <summary>Notifies the doctor linked to the doctor profile.</summary>
    Task PublishToDoctorAsync(Guid doctorId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken);

    /// <summary>Notifies one Identity user, for example the Reception user who requested an extension.</summary>
    Task PublishToUserAsync(Guid userId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken);
}

/// <summary>Used when no publisher is supplied (tests and callers that do not notify). Never delivers anything.</summary>
internal sealed class SilentStaffNotificationPublisher : IStaffNotificationPublisher
{
    public static readonly SilentStaffNotificationPublisher Instance = new();

    public Task PublishToDoctorAsync(Guid doctorId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task PublishToUserAsync(Guid userId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
