using MediCare.Domain.Enums;

namespace MediCare.Application.DTOs.Patients;

public sealed class PatientProfileDto
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    public Gender Gender { get; init; }

    public string? Address { get; init; }

    public string? EmergencyContact { get; init; }

    public string? BloodType { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class UpdatePatientProfileRequest
{
    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unknown;

    public string? Address { get; set; }

    public string? EmergencyContact { get; set; }

    public string? BloodType { get; set; }
}
