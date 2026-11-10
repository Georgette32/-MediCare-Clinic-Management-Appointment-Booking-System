namespace MediCare.Domain.Enums;

/// <summary>
/// Shared appointment lifecycle. Appointment booking itself belongs to another
/// module; this enum is part of the shared database foundation.
/// </summary>
public enum AppointmentStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4
}
