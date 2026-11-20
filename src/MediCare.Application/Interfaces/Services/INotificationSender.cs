
using System;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Services;

public interface INotificationSender
{
    Task SendToUserAsync(
        Guid userId,
        string title,
        string message);
}
