
using Microsoft.AspNetCore.SignalR;

namespace MediCare.Web.Hubs;

public sealed class GuidUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
}
