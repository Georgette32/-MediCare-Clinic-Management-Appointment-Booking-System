using Microsoft.AspNetCore.Identity;

namespace MediCare.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// True only for patients who signed themselves up after email verification was introduced. Such an account can do
    /// nothing but verify until <see cref="IdentityUser{TKey}.EmailConfirmed"/> is true. Existing accounts default to false.
    /// </summary>
    public bool RequiresEmailVerification { get; set; }

    /// <summary>Hash of the current 6-digit email code; null when no code is outstanding.</summary>
    public string? EmailCodeHash { get; set; }
    public DateTimeOffset? EmailCodeExpiresAtUtc { get; set; }
    public DateTimeOffset? EmailCodeSentAtUtc { get; set; }
    public int EmailCodeFailedAttempts { get; set; }
}
