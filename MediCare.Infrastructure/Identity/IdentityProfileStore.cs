using MediCare.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Identity;

public sealed class IdentityProfileStore(UserManager<ApplicationUser> users) : IIdentityProfileStore
{
    private const string DuplicateEmailMessage = "An account with this email already exists.";

    public async Task<IdentityProfileSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var roles = await users.GetRolesAsync(user);
        return new IdentityProfileSnapshot(user.Id, user.UserName ?? string.Empty, user.Email, user.FullName, user.IsActive, roles.ToArray());
    }

    public async Task<IdentityProfileOperationResult> SetFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("The account was not found.");
        user.FullName = fullName;
        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? IdentityProfileOperationResult.Success()
            : IdentityProfileOperationResult.Failure(result.Errors.Select(error => error.Description).ToArray());
    }

    public async Task<IdentityProfileOperationResult> SetEmailAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("The account was not found.");

        // Same fields UserManager maintains for an email change, written directly so a unique-index violation can be
        // reported as a friendly failure by the save below.
        user.Email = email;
        user.NormalizedEmail = email is null ? null : users.NormalizeEmail(email);
        user.EmailConfirmed = false;

        try
        {
            var result = await users.UpdateAsync(user);
            return result.Succeeded
                ? IdentityProfileOperationResult.Success()
                : IdentityProfileOperationResult.Failure(result.Errors.Select(error => error.Description).ToArray());
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            return IdentityProfileOperationResult.Failure(DuplicateEmailMessage);
        }
    }

    private static bool IsUniqueConstraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        return false;
    }
}
