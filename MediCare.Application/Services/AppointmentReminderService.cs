using MediCare.Application.Abstractions;
using MediCare.Application.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Day-before reminders for Confirmed appointments. "Tomorrow" is the clinic-local date from IClinicClock, so the reminder
/// day follows Africa/Cairo rather than UTC. Each reminder carries the DedupKey "reminder:{appointmentId}", so running
/// the job again on the same day never creates a second notification.
/// </summary>
public sealed class AppointmentReminderService(
    IAppointmentReminderRepository reminders,
    IPatientNotificationPublisher publisher,
    IClinicClock clinicClock) : IAppointmentReminderService
{
    public async Task<int> SendDueRemindersAsync(CancellationToken cancellationToken)
    {
        var appointmentDate = clinicClock.Today.AddDays(1);
        var candidates = await reminders.GetConfirmedForDateAsync(appointmentDate, cancellationToken);

        foreach (var candidate in candidates)
        {
            await publisher.PublishAsync(new PatientNotification(
                candidate.PatientId,
                PatientNotificationKind.AppointmentReminder,
                "Appointment tomorrow",
                $"Reminder: you have an appointment with {candidate.DoctorName} tomorrow, {candidate.AppointmentDate:yyyy-MM-dd} at {candidate.StartTime:HH\\:mm}.",
                candidate.AppointmentId,
                $"reminder:{candidate.AppointmentId:D}"), cancellationToken);
        }

        return candidates.Count;
    }
}

public interface IAppointmentReminderService
{
    /// <summary>Publishes reminders for Confirmed appointments on the next clinic-local day. Returns how many were handed to the publisher.</summary>
    Task<int> SendDueRemindersAsync(CancellationToken cancellationToken);
}
