using MediCare.Application.DTOs.Doctors;
using MediCare.Application.Interfaces.Identity;
using MediCare.Domain.Entities;

namespace MediCare.Application.Mappings;

internal static class DoctorMappings
{
    public static DoctorListItemDto ToListItemDto(this DoctorProfile profile, IdentityUserInfo? user) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        FullName = user?.FullName ?? "Unknown user",
        Email = user?.Email ?? string.Empty,
        PhoneNumber = user?.PhoneNumber,
        Specialization = profile.Specialization?.Name ?? string.Empty,
        LicenseNumber = profile.LicenseNumber,
        ConsultationFee = profile.ConsultationFee,
        YearsOfExperience = profile.YearsOfExperience,
        ApprovalStatus = profile.ApprovalStatus,
        RejectionReason = profile.RejectionReason,
        IsActive = user?.IsActive ?? false,
        EmailConfirmed = user?.EmailConfirmed ?? false,
        CreatedAtUtc = profile.CreatedAtUtc,
        ReviewedAtUtc = profile.ReviewedAtUtc
    };

    public static DoctorDetailsDto ToDetailsDto(this DoctorProfile profile, IdentityUserInfo? user) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        FullName = user?.FullName ?? "Unknown user",
        Email = user?.Email ?? string.Empty,
        PhoneNumber = user?.PhoneNumber,
        Specialization = profile.Specialization?.Name ?? string.Empty,
        SpecializationId = profile.SpecializationId,
        LicenseNumber = profile.LicenseNumber,
        ConsultationFee = profile.ConsultationFee,
        YearsOfExperience = profile.YearsOfExperience,
        ApprovalStatus = profile.ApprovalStatus,
        RejectionReason = profile.RejectionReason,
        IsActive = user?.IsActive ?? false,
        EmailConfirmed = user?.EmailConfirmed ?? false,
        CreatedAtUtc = profile.CreatedAtUtc,
        ReviewedAtUtc = profile.ReviewedAtUtc,
        Bio = profile.Bio
    };
}
