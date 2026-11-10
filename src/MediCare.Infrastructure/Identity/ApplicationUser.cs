using Microsoft.AspNetCore.Identity;

namespace MediCare.Infrastructure.Identity;

/// <summary>
/// Identity user. Account activation is intentionally separate from doctor approval:
/// an account can be active while the doctor profile is still pending review.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}
