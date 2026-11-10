using FluentValidation;
using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Users;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;

namespace MediCare.Application.Services;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IIdentityService _identity;
    private readonly IDoctorRepository _doctors;
    private readonly IValidator<ChangeUserRoleRequest> _roleValidator;

    public UserManagementService(
        IIdentityService identity,
        IDoctorRepository doctors,
        IValidator<ChangeUserRoleRequest> roleValidator)
    {
        _identity = identity;
        _doctors = doctors;
        _roleValidator = roleValidator;
    }

    public async Task<PagedResult<UserListItemDto>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _identity.GetUsersAsync(query, cancellationToken);
        var items = new List<UserListItemDto>(page.Items.Count);

        foreach (var user in page.Items)
        {
            var roles = await _identity.GetRolesAsync(user.Id, cancellationToken);
            items.Add(ToDto(user, roles));
        }

        return new PagedResult<UserListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<UserListItemDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _identity.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<UserListItemDto>.Fail("User was not found.");
        }

        var roles = await _identity.GetRolesAsync(userId, cancellationToken);
        return Result<UserListItemDto>.Ok(ToDto(user, roles));
    }

    public async Task<Result> ChangeRoleAsync(Guid administratorId, ChangeUserRoleRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _roleValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Fail(validation.Errors.Select(e => e.ErrorMessage));
        }

        if (administratorId == Guid.Empty)
        {
            return Result.Fail("An administrator is required.");
        }

        if (request.UserId == administratorId)
        {
            return Result.Fail("You cannot change your own role.");
        }

        var target = await _identity.FindByIdAsync(request.UserId, cancellationToken);
        if (target is null)
        {
            return Result.Fail("User was not found.");
        }

        var newRole = Roles.Normalize(request.Role);
        var currentRoles = await _identity.GetRolesAsync(target.Id, cancellationToken);

        if (currentRoles.Count == 1 && string.Equals(currentRoles[0], newRole, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Ok();
        }

        if (currentRoles.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase) && newRole != Roles.Admin)
        {
            var adminCount = await _identity.CountUsersInRoleAsync(Roles.Admin, cancellationToken);
            if (adminCount <= 1)
            {
                return Result.Fail("The last administrator cannot be demoted.");
            }
        }

        if (newRole == Roles.Doctor && !await _doctors.ExistsForUserAsync(target.Id, cancellationToken))
        {
            return Result.Fail("A user can only receive the Doctor role after completing doctor registration.");
        }

        foreach (var role in currentRoles)
        {
            var removed = await _identity.RemoveFromRoleAsync(target.Id, role, cancellationToken);
            if (!removed.Succeeded)
            {
                return removed;
            }
        }

        var added = await _identity.AddToRoleAsync(target.Id, newRole, cancellationToken);
        if (!added.Succeeded)
        {
            // Best-effort rollback so the account is never left without a role.
            foreach (var role in currentRoles)
            {
                await _identity.AddToRoleAsync(target.Id, role, cancellationToken);
            }

            return added;
        }

        await _identity.RefreshSecurityStampAsync(target.Id, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> SetActiveAsync(Guid administratorId, Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (administratorId == Guid.Empty)
        {
            return Result.Fail("An administrator is required.");
        }

        if (userId == administratorId && !isActive)
        {
            return Result.Fail("You cannot deactivate your own account.");
        }

        var target = await _identity.FindByIdAsync(userId, cancellationToken);
        if (target is null)
        {
            return Result.Fail("User was not found.");
        }

        if (!isActive)
        {
            var roles = await _identity.GetRolesAsync(userId, cancellationToken);
            if (roles.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase))
            {
                var adminCount = await _identity.CountUsersInRoleAsync(Roles.Admin, cancellationToken);
                if (adminCount <= 1)
                {
                    return Result.Fail("The last administrator cannot be deactivated.");
                }
            }
        }

        return await _identity.SetActiveAsync(userId, isActive, cancellationToken);
    }

    private static UserListItemDto ToDto(IdentityUserInfo user, IReadOnlyList<string> roles) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        IsActive = user.IsActive,
        EmailConfirmed = user.EmailConfirmed,
        CreatedAtUtc = user.CreatedAtUtc,
        Roles = roles
    };
}
