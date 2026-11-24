namespace MediCare.Application.Enums;

/// <summary>Patient-facing notification kinds. Stored by name in Notifications.Kind.</summary>
public enum PatientNotificationKind
{
    AppointmentCancelledClinicUnavailable,
    AppointmentRescheduledByClinic,
    WalkInCancelledClinicUnavailable,
    AppointmentCancelledDoctorNoLongerAvailable,
    WalkInCancelledDoctorNoLongerAvailable,
    BookingRequestConfirmed,
    BookingRequestRejected,
    RescheduleRequestConfirmed,
    RescheduleRequestRejected,
    AppointmentCancelledByClinic,
    AppointmentReminder
}
