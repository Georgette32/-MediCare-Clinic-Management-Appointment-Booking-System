using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Professional profile attached to an Identity user that holds the Doctor role.
/// Registration creates a pending profile; only administrators may approve it.
/// </summary>
public sealed class DoctorProfile : BaseEntity
{
    private readonly List<DoctorAvailability> _availabilities = new();

    private DoctorProfile()
    {
        // Required by EF Core.
    }

    public DoctorProfile(
        Guid userId,
        Guid specializationId,
        string licenseNumber,
        decimal consultationFee,
        int yearsOfExperience,
        string? bio = null)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("A doctor profile must belong to a user.");
        }

        UserId = userId;
        SetProfessionalDetails(specializationId, licenseNumber, consultationFee, yearsOfExperience, bio);
        ApprovalStatus = DoctorApprovalStatus.Pending;
    }

    public Guid UserId { get; private set; }

    public Guid SpecializationId { get; private set; }

    public Specialization Specialization { get; private set; } = null!;

    public string LicenseNumber { get; private set; } = string.Empty;

    public string? Bio { get; private set; }

    public decimal ConsultationFee { get; private set; }

    public int YearsOfExperience { get; private set; }

    public DoctorApprovalStatus ApprovalStatus { get; private set; } = DoctorApprovalStatus.Pending;

    public string? RejectionReason { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public IReadOnlyCollection<DoctorAvailability> Availabilities => _availabilities;

    public bool IsApproved => ApprovalStatus == DoctorApprovalStatus.Approved;

    public void Approve(Guid administratorId)
    {
        EnsureAdministrator(administratorId);
        ApprovalStatus = DoctorApprovalStatus.Approved;
        RejectionReason = null;
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewedByUserId = administratorId;
        MarkUpdated();
    }

    public void Reject(Guid administratorId, string reason)
    {
        EnsureAdministrator(administratorId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.");
        }

        ApprovalStatus = DoctorApprovalStatus.Rejected;
        RejectionReason = reason.Trim();
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewedByUserId = administratorId;
        MarkUpdated();
    }

    public void Suspend(Guid administratorId, string reason)
    {
        EnsureAdministrator(administratorId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A suspension reason is required.");
        }

        ApprovalStatus = DoctorApprovalStatus.Suspended;
        RejectionReason = reason.Trim();
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewedByUserId = administratorId;
        MarkUpdated();
    }

    public void ReturnToPending()
    {
        ApprovalStatus = DoctorApprovalStatus.Pending;
        RejectionReason = null;
        ReviewedAtUtc = null;
        ReviewedByUserId = null;
        MarkUpdated();
    }

    public void UpdateProfessionalDetails(
        Guid specializationId,
        string licenseNumber,
        decimal consultationFee,
        int yearsOfExperience,
        string? bio)
    {
        SetProfessionalDetails(specializationId, licenseNumber, consultationFee, yearsOfExperience, bio);
        MarkUpdated();
    }

    private void SetProfessionalDetails(
        Guid specializationId,
        string licenseNumber,
        decimal consultationFee,
        int yearsOfExperience,
        string? bio)
    {
        if (specializationId == Guid.Empty)
        {
            throw new DomainException("A specialization is required.");
        }

        if (string.IsNullOrWhiteSpace(licenseNumber))
        {
            throw new DomainException("License number is required.");
        }

        if (licenseNumber.Trim().Length > 60)
        {
            throw new DomainException("License number cannot exceed 60 characters.");
        }

        if (consultationFee < 0)
        {
            throw new DomainException("Consultation fee cannot be negative.");
        }

        if (yearsOfExperience is < 0 or > 70)
        {
            throw new DomainException("Years of experience must be between 0 and 70.");
        }

        SpecializationId = specializationId;
        LicenseNumber = licenseNumber.Trim();
        ConsultationFee = decimal.Round(consultationFee, 2);
        YearsOfExperience = yearsOfExperience;
        Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
    }

    private static void EnsureAdministrator(Guid administratorId)
    {
        if (administratorId == Guid.Empty)
        {
            throw new DomainException("An administrator is required to review a doctor profile.");
        }
    }
}
