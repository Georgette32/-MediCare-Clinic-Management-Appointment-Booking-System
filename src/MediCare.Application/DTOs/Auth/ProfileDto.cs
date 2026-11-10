using MediCare.Domain.Enums;

namespace MediCare.Application.DTOs.Auth;

public sealed class ProfileDto
{
    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; }

    public bool EmailConfirmed { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; }

    public string? Address { get; set; }

    public string? EmergencyContact { get; set; }

    public string? BloodType { get; set; }
}

public sealed class UpdateProfileRequest
{
    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unknown;

    public string? Address { get; set; }

    public string? EmergencyContact { get; set; }

    public string? BloodType { get; set; }
}
