using System.ComponentModel.DataAnnotations;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.DTOs.Specializations;
using MediCare.Application.DTOs.Users;
using MediCare.Domain.Enums;

namespace MediCare.Web.ViewModels.Admin;

public sealed class DoctorListViewModel
{
    public PagedResultView<DoctorListItemDto> Doctors { get; set; } = new();

    public string? Search { get; set; }

    public DoctorApprovalStatus? ApprovalStatus { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

public sealed class PagedResultView<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int TotalCount { get; set; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

public sealed class DoctorEditViewModel
{
    public Guid DoctorId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string CurrentApprovalStatus { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 3)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(30)]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Specialization")]
    public Guid SpecializationId { get; set; }

    [Required, StringLength(60, MinimumLength = 3)]
    [Display(Name = "Medical license number")]
    public string LicenseNumber { get; set; } = string.Empty;

    [Range(0, 100000)]
    [Display(Name = "Consultation fee")]
    public decimal ConsultationFee { get; set; }

    [Range(0, 70)]
    [Display(Name = "Years of experience")]
    public int YearsOfExperience { get; set; }

    [StringLength(2000)]
    public string? Bio { get; set; }

    public IReadOnlyList<SpecializationDto> Specializations { get; set; } = Array.Empty<SpecializationDto>();
}

public sealed class UserListViewModel
{
    public PagedResultView<UserListItemDto> Users { get; set; } = new();

    public string? Search { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

public sealed class SpecializationListViewModel
{
    public IReadOnlyList<SpecializationDto> Specializations { get; set; } = Array.Empty<SpecializationDto>();
}

public sealed class SpecializationFormViewModel
{
    public Guid Id { get; set; }

    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public sealed class RejectDoctorViewModel
{
    public Guid DoctorId { get; set; }

    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
