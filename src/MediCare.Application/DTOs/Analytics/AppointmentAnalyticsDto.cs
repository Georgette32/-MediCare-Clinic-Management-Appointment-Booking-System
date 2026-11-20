
using System;

namespace MediCare.Application.DTOs.Analytics;

public sealed class AppointmentAnalyticsDto
{
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }

    public int TotalAppointments { get; set; }
    public int CompletedAppointments { get; set; }
    public int CancelledAppointments { get; set; }
    public int NoShowAppointments { get; set; }

    public double CompletionRate { get; set; }
    public double CancellationRate { get; set; }
    public double NoShowRate { get; set; }
}
