namespace MediCare.Application.Abstractions;

public interface IIdentityAccountStore
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<IdentityAccountCreateResult> CreateAsync(
        string userName,
        string? email,
        string fullName,
        string? phoneNumber,
        string role,
        string password,
        bool mustChangePassword,
        bool requireEmailVerification,
        CancellationToken cancellationToken);

    /// <summary>The account with its role names, or null when the account does not exist.</summary>
    Task<IdentityAccountSnapshot?> FindAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the password with a temporary one, sets MustChangePassword, ends existing sessions (security stamp)
    /// and clears any lockout. Returns Failure without changing the flags when Identity refuses the password.
    /// </summary>
    Task<IdentityPasswordResetResult> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken);
}

public sealed record IdentityAccountCreateResult(bool Succeeded, Guid UserId, IReadOnlyList<string> Errors)
{
    public static IdentityAccountCreateResult Success(Guid userId) => new(true, userId, []);
    public static IdentityAccountCreateResult Failure(IEnumerable<string> errors) => new(false, Guid.Empty, errors.ToArray());
}

public sealed record IdentityAccountSnapshot(Guid UserId, string UserName, string FullName, bool IsActive, IReadOnlyList<string> Roles);

public sealed record IdentityPasswordResetResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static IdentityPasswordResetResult Success() => new(true, []);
    public static IdentityPasswordResetResult Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}
