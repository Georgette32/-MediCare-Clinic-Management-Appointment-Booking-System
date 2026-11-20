
using System;

namespace MediCare.Application.DTOs.Notifications;

public sealed class AppointmentReminderEmailDto
{
    public Guid ReminderId { get; set; }

    public string PatientEmail { get; set; } = string.Empty;

    public string PatientName { get; set; } = string.Empty;

    public DateTime AppointmentAtUtc { get; set; }
}
