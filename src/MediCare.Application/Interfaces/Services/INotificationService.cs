
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Services;

public interface INotificationService
{
    Task SendAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type);

    Task<IReadOnlyList<Notification>> GetUserNotificationsAsync(
        Guid userId);

    Task<bool> MarkAsReadAsync(
        Guid notificationId,
        Guid userId);

    Task<int> GetUnreadCountAsync(Guid userId);
}
