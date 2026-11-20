using MediCare.Application.DTOs.Dashboard;

namespace MediCare.Application.Interfaces.Services;

public interface IDashboardService
{
    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
}
