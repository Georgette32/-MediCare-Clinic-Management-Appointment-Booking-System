using System.ComponentModel.DataAnnotations;
using MediCare.Application.Abstractions;

namespace MediCare.Web.Models;

public sealed class DoctorAccountViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public Guid? SpecializationId { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int? AppointmentDurationMinutes { get; set; }

    public IReadOnlyList<SpecializationChoice> Specializations { get; set; } = [];
}

public sealed class StaffAccountViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;
}

public sealed class PatientAccountViewModel
{
    [Required, StringLength(32)]
    [Display(Name = "Egyptian mobile number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;
}

public sealed class PatientRegistrationViewModel
{
    [Required, StringLength(32)]
    [Display(Name = "Egyptian mobile number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress, StringLength(256)]
    public string? Email { get; set; }

    [Required, DataType(DataType.Password), MinLength(12)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class AccountCreatedViewModel
{
    public string FullName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string TemporaryPassword { get; init; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    [Display(Name = "Temporary password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(12)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
