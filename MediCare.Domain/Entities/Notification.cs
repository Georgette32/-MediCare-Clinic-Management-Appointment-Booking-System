namespace MediCare.Domain.Entities;

/// <summary>
/// An in-app notification for one user. Rows are written by the notification provider after the business change has
/// committed. DedupKey, when set, makes the notification unique per recipient (for example one reminder per appointment).
/// </summary>
public sealed class Notification
{
    public const int MaxKindLength = 48;
    public const int MaxTitleLength = 150;
    public const int MaxMessageLength = 1000;
    public const int MaxRelatedEntityTypeLength = 48;
    public const int MaxDedupKeyLength = 150;

    private Notification() { }

    public Guid Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? RelatedEntityType { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public string? DedupKey { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    public bool IsRead => ReadAtUtc is not null;

    /// <summary>
    /// Creates an unread notification. Over-long messages are truncated rather than refused, so a long free-text reason
    /// never prevents the patient from being told about a change.
    /// </summary>
    public static Notification Create(
        Guid recipientUserId,
        string kind,
        string title,
        string message,
        string? relatedEntityType,
        Guid? relatedEntityId,
        string? dedupKey,
        DateTimeOffset createdAtUtc)
    {
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("A notification needs a recipient.", nameof(recipientUserId));
        var trimmedKind = kind?.Trim() ?? string.Empty;
        if (trimmedKind.Length == 0 || trimmedKind.Length > MaxKindLength)
            throw new ArgumentException($"The notification kind is required and cannot exceed {MaxKindLength} characters.", nameof(kind));
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0 || trimmedTitle.Length > MaxTitleLength)
            throw new ArgumentException($"The notification title is required and cannot exceed {MaxTitleLength} characters.", nameof(title));
        var trimmedMessage = message?.Trim() ?? string.Empty;
        if (trimmedMessage.Length == 0)
            throw new ArgumentException("The notification message is required.", nameof(message));
        if (trimmedMessage.Length > MaxMessageLength)
            trimmedMessage = trimmedMessage[..MaxMessageLength];

        var type = string.IsNullOrWhiteSpace(relatedEntityType) ? null : relatedEntityType.Trim();
        if (type is not null && type.Length > MaxRelatedEntityTypeLength)
            throw new ArgumentException($"The related entity type cannot exceed {MaxRelatedEntityTypeLength} characters.", nameof(relatedEntityType));
        if ((type is null) != (relatedEntityId is null))
            throw new ArgumentException("A related entity needs both its type and its id, or neither.");

        var key = string.IsNullOrWhiteSpace(dedupKey) ? null : dedupKey.Trim();
        if (key is not null && key.Length > MaxDedupKeyLength)
            throw new ArgumentException($"The dedup key cannot exceed {MaxDedupKeyLength} characters.", nameof(dedupKey));

        return new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            Kind = trimmedKind,
            Title = trimmedTitle,
            Message = trimmedMessage,
            RelatedEntityType = type,
            RelatedEntityId = relatedEntityId,
            DedupKey = key,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    /// <summary>Marks the notification read. Idempotent: the first read time is kept.</summary>
    public void MarkRead(DateTimeOffset readAtUtc)
    {
        ReadAtUtc ??= readAtUtc.ToUniversalTime();
    }
}
