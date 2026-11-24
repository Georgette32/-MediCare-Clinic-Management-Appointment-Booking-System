using MediCare.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Identity;

public sealed class IdentityAccountStore(UserManager<ApplicationUser> users) : IIdentityAccountStore
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await users.FindByEmailAsync(email) is not null;
    }

    public async Task<IdentityAccountCreateResult> CreateAsync(
        string userName,
        string? email,
        string fullName,
        string? phoneNumber,
        string role,
        string password,
        bool mustChangePassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            IsActive = true,
            MustChangePassword = mustChangePassword
        };

        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            return IdentityAccountCreateResult.Failure(["An account with the provided details already exists."]);
        }

        if (!created.Succeeded)
            return IdentityAccountCreateResult.Failure(created.Errors.Select(error => error.Description));

        var assigned = await users.AddToRoleAsync(user, role);
        if (!assigned.Succeeded)
            return IdentityAccountCreateResult.Failure(assigned.Errors.Select(error => error.Description));

        return IdentityAccountCreateResult.Success(user.Id);
    }

    public async Task<IdentityAccountSnapshot?> FindAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var roles = await users.GetRolesAsync(user);
        return new IdentityAccountSnapshot(user.Id, user.UserName ?? string.Empty, user.FullName, user.IsActive, roles.ToArray());
    }

    public async Task<IdentityPasswordResetResult> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("The account was not found.");

        // Identity validates the password against the policy and refuses before anything is persisted.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, temporaryPassword);
        if (!reset.Succeeded)
            return IdentityPasswordResetResult.Failure(reset.Errors.Select(error => error.Description));

        // Flags, lockout and a new security stamp (ends existing sessions) are saved in one user update.
        user.MustChangePassword = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        var stamped = await users.UpdateSecurityStampAsync(user);
        return stamped.Succeeded
            ? IdentityPasswordResetResult.Success()
            : IdentityPasswordResetResult.Failure(stamped.Errors.Select(error => error.Description));
    }

    private static bool IsUniqueConstraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        return false;
    }
}
