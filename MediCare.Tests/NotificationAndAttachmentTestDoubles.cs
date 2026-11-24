using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using System.Data;

namespace MediCare.Tests;

/// <summary>In-memory notification store. Uniqueness of DedupKey is enforced by the service; the store only records rows.</summary>
internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    public Dictionary<Guid, Guid> PatientUsers { get; } = [];
    public Dictionary<Guid, Guid> DoctorUsers { get; } = [];
    public List<Notification> Rows { get; } = [];

    public Task<Guid?> GetPatientUserIdAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult(PatientUsers.TryGetValue(patientId, out var userId) ? (Guid?)userId : null);

    public Task<Guid?> GetDoctorUserIdAsync(Guid doctorId, CancellationToken cancellationToken) =>
        Task.FromResult(DoctorUsers.TryGetValue(doctorId, out var userId) ? (Guid?)userId : null);

    public Task<bool> ExistsForDedupKeyAsync(Guid recipientUserId, string dedupKey, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Any(item => item.RecipientUserId == recipientUserId && item.DedupKey == dedupKey));

    public Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        Rows.Add(notification);
        return Task.CompletedTask;
    }

    public void Detach(Notification notification) => Rows.Remove(notification);

    public Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var mine = Rows.Where(item => item.RecipientUserId == userId).OrderByDescending(item => item.CreatedAtUtc).ToArray();
        IReadOnlyList<Notification> items = mine.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Task.FromResult((items, mine.Length));
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Count(item => item.RecipientUserId == userId && !item.IsRead));

    public Task<Notification?> GetForRecipientAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.SingleOrDefault(item => item.Id == notificationId && item.RecipientUserId == userId));

    public Task<IReadOnlyList<Notification>> GetUnreadForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Notification>>(Rows.Where(item => item.RecipientUserId == userId && !item.IsRead).ToArray());
}

/// <summary>A provider that always fails, for proving that delivery failures never fail the business operation.</summary>
internal sealed class FailingNotificationProvider : INotificationProvider
{
    public int Attempts { get; private set; }

    public Task<bool> DeliverAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        Attempts++;
        throw new InvalidOperationException("The notification provider is unavailable.");
    }
}

internal sealed class RecordingStaffNotificationPublisher : IStaffNotificationPublisher
{
    public List<(Guid Id, StaffNotificationKind Kind, string Title, string Message, Guid? RelatedId)> ToDoctors { get; } = [];
    public List<(Guid Id, StaffNotificationKind Kind, string Title, string Message, Guid? RelatedId)> ToUsers { get; } = [];

    public Task PublishToDoctorAsync(Guid doctorId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken)
    {
        ToDoctors.Add((doctorId, kind, title, message, relatedEntityId));
        return Task.CompletedTask;
    }

    public Task PublishToUserAsync(Guid userId, StaffNotificationKind kind, string title, string message, string? relatedEntityType, Guid? relatedEntityId, CancellationToken cancellationToken)
    {
        ToUsers.Add((userId, kind, title, message, relatedEntityId));
        return Task.CompletedTask;
    }
}

/// <summary>In-memory file store with the same contract as the filesystem store: names must be valid, files are never overwritten.</summary>
internal sealed class InMemoryMedicalFileStore : IMedicalFileStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public async Task<MedicalFileSaveResult> SaveAsync(Stream content, string storedFileName, CancellationToken cancellationToken)
    {
        if (!MedicalAttachment.IsValidStoredFileName(storedFileName))
            throw new ArgumentException("Invalid stored file name.", nameof(storedFileName));
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        Files.Add(storedFileName, bytes);
        return new MedicalFileSaveResult(bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(Files[storedFileName], writable: false));

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken)
    {
        Files.Remove(storedFileName);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryMedicalAttachmentRepository(InMemoryMedicalRecordRepository records) : IMedicalAttachmentRepository
{
    public Task<MedicalAttachment?> GetByIdAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        Task.FromResult(records.Records.Values.SelectMany(record => record.Attachments).SingleOrDefault(item => item.Id == attachmentId));
}

/// <summary>A unit of work whose transaction fails, for the file compensation tests.</summary>
internal sealed class ThrowingUnitOfWork(Exception exception) : IUnitOfWork
{
    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        throw exception;

    public Task SaveChangesAsync(CancellationToken cancellationToken) => throw exception;

    public Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        throw exception;

    public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        throw exception;

    public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        throw exception;
}
