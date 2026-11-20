
using System;

namespace MediCare.Application.DTOs.Analytics;

public sealed class DoctorPerformanceDto
{
    public Guid DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;

    public int TotalAppointments { get; set; }
    public int CompletedAppointments { get; set; }
    public int CancelledAppointments { get; set; }
    public int NoShowAppointments { get; set; }

    public double CompletionRate { get; set; }
}
