using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

public sealed class PatientProfile : BaseEntity
{
    private PatientProfile()
    {
        // Required by EF Core.
    }

    public PatientProfile(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("A patient profile must belong to a user.");
        }

        UserId = userId;
    }

    public Guid UserId { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public Gender Gender { get; private set; } = Gender.Unknown;

    public string? Address { get; private set; }

    public string? EmergencyContact { get; private set; }

    public string? BloodType { get; private set; }

    public void UpdateMedicalInformation(
        DateOnly? dateOfBirth,
        Gender gender,
        string? address,
        string? emergencyContact,
        string? bloodType)
    {
        if (dateOfBirth.HasValue && dateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new DomainException("Date of birth cannot be in the future.");
        }

        DateOfBirth = dateOfBirth;
        Gender = gender;
        Address = Normalize(address);
        EmergencyContact = Normalize(emergencyContact);
        BloodType = Normalize(bloodType);
        MarkUpdated();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
