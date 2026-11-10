using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Users;
using MediCare.Application.Interfaces.Identity;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly ApplicationDbContext _dbContext;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public IdentityService(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        RoleManager<IdentityRole<Guid>> roles,
        ApplicationDbContext dbContext,
        IOptions<IdentityOptions> identityOptions)
    {
        _users = users;
        _signIn = signIn;
        _roles = roles;
        _dbContext = dbContext;
        _identityOptions = identityOptions;
    }

    public bool RequiresEmailConfirmation => _identityOptions.Value.SignIn.RequireConfirmedEmail;

    public async Task<Result<Guid>> CreateUserAsync(
        string email,
        string password,
        string fullName,
        string? phoneNumber,
        string role,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            EmailConfirmed = !RequiresEmailConfirmation,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return Result<Guid>.Fail(created.Errors.Select(e => e.Description));
        }

        var roleResult = await EnsureRoleAsync(role, cancellationToken);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);
            return Result<Guid>.Fail(roleResult.Errors);
        }

        var added = await _users.AddToRoleAsync(user, role);
        if (!added.Succeeded)
        {
            await _users.DeleteAsync(user);
            return Result<Guid>.Fail(added.Errors.Select(e => e.Description));
        }

        return Result<Guid>.Ok(user.Id);
    }

    public async Task<SignInOutcome> PasswordSignInAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null)
        {
            return SignInOutcome.InvalidCredentials;
        }

        if (!user.IsActive)
        {
            return SignInOutcome.Inactive;
        }

        if (RequiresEmailConfirmation && !user.EmailConfirmed)
        {
            return SignInOutcome.EmailNotConfirmed;
        }

        var result = await _signIn.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return SignInOutcome.Success;
        }

        if (result.IsLockedOut)
        {
            return SignInOutcome.LockedOut;
        }

        if (result.IsNotAllowed)
        {
            return SignInOutcome.EmailNotConfirmed;
        }

        return SignInOutcome.InvalidCredentials;
    }

    public Task SignOutAsync() => _signIn.SignOutAsync();

    public async Task<Result> AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        var roleResult = await EnsureRoleAsync(role, cancellationToken);
        if (!roleResult.Succeeded)
        {
            return roleResult;
        }

        var result = await _users.AddToRoleAsync(user, role);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(result.Errors.Select(e => e.Description));
    }

    public async Task<Result> RemoveFromRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        var result = await _users.RemoveFromRoleAsync(user, role);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(result.Errors.Select(e => e.Description));
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user is null ? Array.Empty<string>() : (await _users.GetRolesAsync(user)).ToList();
    }

    public async Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user is null ? null : ToInfo(user);
    }

    public async Task<IdentityUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(email);
        return user is null ? null : ToInfo(user);
    }

    public async Task<IReadOnlyList<IdentityUserInfo>> GetUsersByIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<IdentityUserInfo>();
        }

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(cancellationToken);

        return users.Select(ToInfo).ToList();
    }

    public async Task<Result<string>> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<string>.Fail("User was not found.");
        }

        if (user.EmailConfirmed)
        {
            return Result<string>.Fail("Email is already confirmed.");
        }

        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        return Result<string>.Ok(token);
    }

    public async Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("The confirmation link is invalid.");
        }

        var result = await _users.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail("The confirmation link is invalid or has expired.");
    }

    public async Task<Result<string>> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            return Result<string>.Fail("Account was not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        return Result<string>.Ok(token);
    }

    public async Task<Result> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null)
        {
            return Result.Fail("The reset link is invalid or has expired.");
        }

        var result = await _users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            return Result.Fail(result.Errors.Select(e => e.Description));
        }

        await _users.UpdateSecurityStampAsync(user);
        return Result.Ok();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return Result.Fail(result.Errors.Select(e => e.Description));
        }

        await _signIn.RefreshSignInAsync(user);
        return Result.Ok();
    }

    public async Task<Result> UpdateAccountAsync(Guid userId, string fullName, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        user.FullName = fullName.Trim();
        user.PhoneNumber = phoneNumber;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var result = await _users.UpdateAsync(user);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(result.Errors.Select(e => e.Description));
    }

    public async Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        user.IsActive = isActive;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Result.Fail(result.Errors.Select(e => e.Description));
        }

        // Invalidate existing cookies; the request middleware also blocks inactive users immediately.
        await _users.UpdateSecurityStampAsync(user);
        return Result.Ok();
    }

    public async Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user?.IsActive ?? false;
    }

    public async Task<Result> SetEmailConfirmedAsync(Guid userId, bool emailConfirmed, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        user.EmailConfirmed = emailConfirmed;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Result.Fail(result.Errors.Select(e => e.Description));
        }

        await _users.UpdateSecurityStampAsync(user);
        return Result.Ok();
    }

    public async Task<Result> RefreshSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Fail("User was not found.");
        }

        var result = await _users.UpdateSecurityStampAsync(user);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(result.Errors.Select(e => e.Description));
    }

    public async Task<PagedResult<IdentityUserInfo>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<ApplicationUser> users = _dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u =>
                u.FullName.Contains(term) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var normalizedRole = query.Role.Trim().ToUpperInvariant();
            users = users.Where(u => _dbContext.UserRoles.Any(ur =>
                ur.UserId == u.Id &&
                _dbContext.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalizedRole)));
        }

        if (query.IsActive.HasValue)
        {
            users = users.Where(u => u.IsActive == query.IsActive.Value);
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 10 : query.PageSize;

        var total = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<IdentityUserInfo>(items.Select(ToInfo).ToList(), page, pageSize, total);
    }

    public async Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default)
    {
        var normalized = role.Trim().ToUpperInvariant();
        var roleEntity = await _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.NormalizedName == normalized, cancellationToken);

        return roleEntity is null
            ? 0
            : await _dbContext.UserRoles.CountAsync(ur => ur.RoleId == roleEntity.Id, cancellationToken);
    }

    public Task<int> CountUsersAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Users.CountAsync(cancellationToken);

    public Task<int> CountActiveUsersAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Users.CountAsync(u => u.IsActive, cancellationToken);

    public async Task<Result> EnsureRoleAsync(string role, CancellationToken cancellationToken = default)
    {
        if (!Roles.IsValid(role))
        {
            return Result.Fail("Unknown role.");
        }

        var normalized = Roles.Normalize(role);
        if (await _roles.RoleExistsAsync(normalized))
        {
            return Result.Ok();
        }

        var result = await _roles.CreateAsync(new IdentityRole<Guid>(normalized) { Id = Guid.NewGuid() });
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(result.Errors.Select(e => e.Description));
    }

    public async Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Ok();
        }

        var result = await _users.DeleteAsync(user);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(result.Errors.Select(e => e.Description));
    }

    private static IdentityUserInfo ToInfo(ApplicationUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.FullName,
        user.PhoneNumber,
        user.IsActive,
        user.EmailConfirmed,
        user.CreatedAtUtc);
}
