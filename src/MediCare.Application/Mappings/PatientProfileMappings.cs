using MediCare.Application.DTOs.Patients;
using MediCare.Application.Interfaces.Identity;
using MediCare.Domain.Entities;

namespace MediCare.Application.Mappings;

internal static class PatientProfileMappings
{
    public static PatientProfileDto ToDto(this PatientProfile profile, IdentityUserInfo? user) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        FullName = user?.FullName ?? "Unknown user",
        Email = user?.Email ?? string.Empty,
        PhoneNumber = user?.PhoneNumber,
        DateOfBirth = profile.DateOfBirth,
        Gender = profile.Gender,
        Address = profile.Address,
        EmergencyContact = profile.EmergencyContact,
        BloodType = profile.BloodType,
        CreatedAtUtc = profile.CreatedAtUtc
    };
}
