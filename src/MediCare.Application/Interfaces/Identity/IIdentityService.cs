using MediCare.Application.Common;
using MediCare.Application.DTOs.Users;

namespace MediCare.Application.Interfaces.Identity;

public enum SignInOutcome
{
    Success = 0,
    InvalidCredentials = 1,
    EmailNotConfirmed = 2,
    Inactive = 3,
    LockedOut = 4,
    NotAllowed = 5
}

public sealed record IdentityUserInfo(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    bool IsActive,
    bool EmailConfirmed,
    DateTime CreatedAtUtc);

/// <summary>
/// Application-facing abstraction over ASP.NET Core Identity. Infrastructure owns
/// password hashing, tokens, cookies and the Identity schema.
/// </summary>
public interface IIdentityService
{
    bool RequiresEmailConfirmation { get; }

    Task<Result<Guid>> CreateUserAsync(
        string email,
        string password,
        string fullName,
        string? phoneNumber,
        string role,
        CancellationToken cancellationToken = default);

    Task<SignInOutcome> PasswordSignInAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken = default);

    Task SignOutAsync();

    Task<Result> AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);

    Task<Result> RemoveFromRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IdentityUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IdentityUserInfo>> GetUsersByIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);

    Task<Result<string>> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<Result<string>> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task<Result> UpdateAccountAsync(Guid userId, string fullName, string? phoneNumber, CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> SetEmailConfirmedAsync(Guid userId, bool emailConfirmed, CancellationToken cancellationToken = default);

    Task<Result> RefreshSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<IdentityUserInfo>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default);

    Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default);

    Task<int> CountUsersAsync(CancellationToken cancellationToken = default);

    Task<int> CountActiveUsersAsync(CancellationToken cancellationToken = default);

    Task<Result> EnsureRoleAsync(string role, CancellationToken cancellationToken = default);

    Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
