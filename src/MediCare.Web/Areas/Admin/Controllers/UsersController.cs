using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Users;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class UsersController : Controller
{
    private readonly IUserManagementService _users;
    private readonly ICurrentUserService _currentUser;

    public UsersController(IUserManagementService users, ICurrentUserService currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(UserQuery query, CancellationToken cancellationToken)
    {
        var page = await _users.GetUsersAsync(query, cancellationToken);
        var model = new UserListViewModel
        {
            Search = query.Search,
            Role = query.Role,
            IsActive = query.IsActive,
            Page = page.Page,
            PageSize = page.PageSize,
            Users = new PagedResultView<UserListItemDto>
            {
                Items = page.Items,
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount
            }
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _users.ChangeRoleAsync(_currentUser.UserId ?? Guid.Empty, request, cancellationToken);
        SetResultMessage(result, "The user's role was updated.", "Could not change the user's role.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _users.SetActiveAsync(_currentUser.UserId ?? Guid.Empty, id, true, cancellationToken);
        SetResultMessage(result, "User activated.", "Could not activate the user.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _users.SetActiveAsync(_currentUser.UserId ?? Guid.Empty, id, false, cancellationToken);
        SetResultMessage(result, "User deactivated.", "Could not deactivate the user.");
        return RedirectToAction(nameof(Index));
    }

    private void SetResultMessage(Result result, string success, string failure)
    {
        if (result.Succeeded)
        {
            TempData["Success"] = success;
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors.DefaultIfEmpty(failure));
        }
    }
}
