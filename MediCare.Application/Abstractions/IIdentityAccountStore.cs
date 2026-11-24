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
        CancellationToken cancellationToken);
}

public sealed record IdentityAccountCreateResult(bool Succeeded, Guid UserId, IReadOnlyList<string> Errors)
{
    public static IdentityAccountCreateResult Success(Guid userId) => new(true, userId, []);
    public static IdentityAccountCreateResult Failure(IEnumerable<string> errors) => new(false, Guid.Empty, errors.ToArray());
}
