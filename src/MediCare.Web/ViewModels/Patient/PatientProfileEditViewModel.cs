using System.ComponentModel.DataAnnotations;
using MediCare.Domain.Enums;

namespace MediCare.Web.ViewModels.Patient;

public sealed class PatientProfileEditViewModel
{
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 3)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Date of birth")]
    public DateOnly? DateOfBirth { get; set; }

    [Display(Name = "Gender")]
    public Gender Gender { get; set; } = Gender.Unknown;

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(200)]
    [Display(Name = "Emergency contact")]
    public string? EmergencyContact { get; set; }

    [StringLength(5)]
    [Display(Name = "Blood type")]
    public string? BloodType { get; set; }
}
