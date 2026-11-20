
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MediCare.Application.Services;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationSender _notificationSender;

    public NotificationService(
        INotificationRepository notifications,
        IUnitOfWork unitOfWork,
        INotificationSender notificationSender)
    {
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _notificationSender = notificationSender;
    }

    public async Task SendAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type)
    {
        var notification = new Notification(
            userId,
            title,
            message,
            type);

        await _notifications.AddAsync(notification);
        await _unitOfWork.SaveChangesAsync();

        await _notificationSender.SendToUserAsync(
            userId,
            title,
            message);
    }

    public Task<IReadOnlyList<Notification>> GetUserNotificationsAsync(
        Guid userId)
    {
        return _notifications.GetByUserIdAsync(userId);
    }

    public async Task<bool> MarkAsReadAsync(
        Guid notificationId,
        Guid userId)
    {
        var notification = await _notifications.GetByIdAsync(notificationId);

        if (notification is null || notification.UserId != userId)
            return false;

        notification.MarkAsRead();

        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public Task<int> GetUnreadCountAsync(Guid userId)
    {
        return _notifications.GetUnreadCountAsync(userId);
    }
}
