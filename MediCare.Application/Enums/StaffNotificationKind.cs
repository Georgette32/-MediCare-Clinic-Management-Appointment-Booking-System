namespace MediCare.Application.Enums;

/// <summary>Clinician and staff notification kinds. Stored by name in Notifications.Kind.</summary>
public enum StaffNotificationKind
{
    WalkInAdmissionRequested,
    WorkingHourExtensionRequested,
    WorkingHourExtensionDecided,
    /// <summary>A patient asked for an appointment; Reception confirms or rejects it.</summary>
    AppointmentRequested,
    /// <summary>A patient asked to move an appointment; Reception confirms or rejects the replacement.</summary>
    RescheduleRequested,
    /// <summary>The doctor approved or rejected a walk-in admission that Reception asked for.</summary>
    WalkInAdmissionDecided,
    /// <summary>Reception withdrew a pending working-hour extension request before the doctor decided.</summary>
    WorkingHourExtensionWithdrawn,
    /// <summary>The doctor cancelled an extension that Reception had requested.</summary>
    WorkingHourExtensionCancelled
}
