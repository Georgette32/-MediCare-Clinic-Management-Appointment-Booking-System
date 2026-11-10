namespace MediCare.Domain.Enums;

/// <summary>
/// Lifecycle of a doctor application. A newly registered doctor always starts as
/// <see cref="Pending"/> and receives no clinical privileges until an administrator
/// approves the profile.
/// </summary>
public enum DoctorApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Suspended = 3
}
