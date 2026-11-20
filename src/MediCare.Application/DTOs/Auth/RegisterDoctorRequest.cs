namespace MediCare.Application.DTOs.Auth;

public sealed class RegisterDoctorRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmPassword { get; set; } = string.Empty;

    public Guid SpecializationId { get; set; }

    public string LicenseNumber { get; set; } = string.Empty;

    public decimal ConsultationFee { get; set; }

    public int YearsOfExperience { get; set; }

    public string? Bio { get; set; }
}
