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

    private static bool IsUniqueConstraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        return false;
    }
}
