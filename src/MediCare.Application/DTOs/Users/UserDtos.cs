namespace MediCare.Application.DTOs.Users;

public sealed class UserQuery
{
    public string? Search { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

public sealed class UserListItemDto
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; }

    public bool EmailConfirmed { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

public sealed class ChangeUserRoleRequest
{
    public Guid UserId { get; set; }

    public string Role { get; set; } = string.Empty;
}
