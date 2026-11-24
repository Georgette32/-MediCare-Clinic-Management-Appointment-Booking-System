namespace MediCare.Web.Models;

/// <summary>Confirm page for a password reset. Holds only what the page displays and the id to post back.</summary>
public sealed class PasswordResetConfirmModel
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}
