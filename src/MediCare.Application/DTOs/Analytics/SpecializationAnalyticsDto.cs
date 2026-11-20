
using System;

namespace MediCare.Application.DTOs.Analytics;

public sealed class SpecializationAnalyticsDto
{
    public Guid SpecializationId { get; set; }
    public string SpecializationName { get; set; } = string.Empty;
    public int AppointmentCount { get; set; }
}
