
using MediCare.Domain.Enums;
using System;

namespace MediCare.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid UserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public NotificationType Type { get; private set; }

    public bool IsRead { get; private set; }

    public DateTime? ReadAtUtc { get; private set; }

    private Notification()
    {
    }

    public Notification(
        Guid userId,
        string title,
        string message,
        NotificationType type)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required.");

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.");

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.");

        UserId = userId;
        Title = title;
        Message = message;
        Type = type;
        IsRead = false;
    }

    public void MarkAsRead()
    {
        if (IsRead)
            return;

        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }
}
