using MediCare.Domain.Enums;

namespace MediCare.Application.DTOs.Doctors;

public sealed class DoctorQuery
{
    public string? Search { get; set; }

    public DoctorApprovalStatus? ApprovalStatus { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

public class DoctorListItemDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string Specialization { get; set; } = string.Empty;

    public string LicenseNumber { get; set; } = string.Empty;

    public decimal ConsultationFee { get; set; }

    public int YearsOfExperience { get; set; }

    public DoctorApprovalStatus ApprovalStatus { get; set; }

    public string? RejectionReason { get; set; }

    public bool IsActive { get; set; }

    public bool EmailConfirmed { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
}

public sealed class DoctorDetailsDto : DoctorListItemDto
{
    public string? Bio { get; set; }

    public Guid SpecializationId { get; set; }
}

public sealed class UpdateDoctorRequest
{
    public Guid DoctorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public Guid SpecializationId { get; set; }

    public string LicenseNumber { get; set; } = string.Empty;

    public decimal ConsultationFee { get; set; }

    public int YearsOfExperience { get; set; }

    public string? Bio { get; set; }
}
