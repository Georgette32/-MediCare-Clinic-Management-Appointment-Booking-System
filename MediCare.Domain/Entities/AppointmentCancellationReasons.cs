namespace MediCare.Domain.Entities;

/// <summary>
/// Reasons written to Appointment.CancellationReason by the system. Clinic-unavailable cancellations are never
/// No-Shows and are excluded from queueing analytics, so the exact text is kept in one place.
/// </summary>
public static class AppointmentCancellationReasons
{
    public const string ClinicUnavailable = "clinic unavailable";

    /// <summary>A Pending replacement cancelled because its original appointment was cancelled.</summary>
    public const string OriginalCancelled = "original appointment cancelled";

    /// <summary>
    /// Set by maintenance on a previous-day Confirmed appointment whose patient checked in but left before any
    /// consultation completed (the attendance attempt was cancelled or closed without a consultation).
    /// </summary>
    public const string PatientLeftBeforeConsultation = "patient left before consultation";

    /// <summary>Set on every live appointment of a doctor who was deactivated.</summary>
    public const string DoctorNoLongerAvailable = "doctor no longer available";
}
