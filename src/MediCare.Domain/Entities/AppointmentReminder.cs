
using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using System;

namespace MediCare.Domain.Entities;

public class AppointmentReminder : BaseEntity
{
    public Guid AppointmentId { get; private set; }
    public DateTime ScheduledForUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public AppointmentReminderStatus Status { get; private set; }

    private AppointmentReminder() { }

    public AppointmentReminder(Guid appointmentId, DateTime scheduledForUtc)
    {
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("Appointment ID is required.");

        AppointmentId = appointmentId;
        ScheduledForUtc = scheduledForUtc;
        Status = AppointmentReminderStatus.Pending;
    }

    public void MarkAsSent()
    {
        if (Status != AppointmentReminderStatus.Pending)
            throw new InvalidOperationException("Only pending reminders can be sent.");

        Status = AppointmentReminderStatus.Sent;
        SentAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }

    public void MarkAsFailed()
    {
        if (Status != AppointmentReminderStatus.Pending)
            throw new InvalidOperationException("Only pending reminders can fail.");

        Status = AppointmentReminderStatus.Failed;
        MarkUpdated();
    }
}
