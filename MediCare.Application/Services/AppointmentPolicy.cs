using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Action rules shared by the domain checks, the booking pages (which decide what to show) and the tests.
/// Dates are clinic-local: the appointment day starts at 00:00 in the clinic timezone, so "before the day" means the
/// clinic-local date is earlier than the appointment date. Callers pass the clinic date from IClinicClock.
/// </summary>
public static class AppointmentPolicy
{
    public static bool IsLive(AppointmentStatus status) => status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;

    /// <summary>Patients may cancel a Pending or Confirmed appointment only before its calendar day.</summary>
    public static bool CanPatientCancel(AppointmentStatus status, DateOnly appointmentDate, DateOnly clinicToday) =>
        IsLive(status) && clinicToday < appointmentDate;

    /// <summary>Patients may reschedule a Confirmed appointment before its day while no live replacement exists.</summary>
    public static bool CanPatientReschedule(AppointmentStatus status, DateOnly appointmentDate, DateOnly clinicToday, bool hasLiveReplacement) =>
        status == AppointmentStatus.Confirmed && clinicToday < appointmentDate && !hasLiveReplacement;

    /// <summary>Reception and Admin may cancel any live appointment from its day onward (with a reason).</summary>
    public static bool CanClinicCancel(AppointmentStatus status, DateOnly appointmentDate, DateOnly clinicToday) =>
        IsLive(status) && appointmentDate >= clinicToday;

    /// <summary>Reception and Admin may move a Confirmed appointment from its day onward while no live replacement exists.</summary>
    public static bool CanClinicReschedule(AppointmentStatus status, DateOnly appointmentDate, DateOnly clinicToday, bool hasLiveReplacement) =>
        status == AppointmentStatus.Confirmed && appointmentDate >= clinicToday && !hasLiveReplacement;

    /// <summary>Refuses bookings for dates or times already past in clinic-local time.</summary>
    public static void EnsureNotInPast(DateOnly date, TimeOnly startTime, DateOnly clinicToday, TimeOnly clinicCurrentTime)
    {
        if (date < clinicToday)
            throw new InvalidOperationException("Appointments cannot be booked for a past date.");
        if (date == clinicToday && startTime < clinicCurrentTime)
            throw new InvalidOperationException("That time has already passed today.");
    }
}
