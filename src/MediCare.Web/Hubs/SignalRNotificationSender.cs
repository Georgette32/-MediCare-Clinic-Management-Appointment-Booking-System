
using MediCare.Application.Interfaces.Services;
using MediCare.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace MediCare.Web.Hubs;

public sealed class SignalRNotificationSender : INotificationSender
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public SignalRNotificationSender(
        IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task SendToUserAsync(
        Guid userId,
        string title,
        string message)
    {
        return _hubContext.Clients
            .Group(userId.ToString())
            .SendAsync("ReceiveNotification", new
            {
                Title = title,
                Message = message,
                SentAtUtc = DateTime.UtcNow
            });
    }
}
