using MediCare.Application.Common;
using MediCare.Application.DTOs.Users;

namespace MediCare.Application.Interfaces.Services;

public interface IUserManagementService
{
    Task<PagedResult<UserListItemDto>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default);

    Task<Result<UserListItemDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ChangeRoleAsync(Guid administratorId, ChangeUserRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(Guid administratorId, Guid userId, bool isActive, CancellationToken cancellationToken = default);
}
