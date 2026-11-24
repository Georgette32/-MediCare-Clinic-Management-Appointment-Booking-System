using System.ComponentModel.DataAnnotations;

namespace MediCare.Infrastructure.Maintenance;

/// <summary>
/// Bound from Clinic:Maintenance. There is deliberately no code default: IntervalMinutes must be configured and
/// positive, and the application refuses to start otherwise.
/// </summary>
public sealed class DisruptionMaintenanceOptions
{
    [Range(1, int.MaxValue)]
    public int IntervalMinutes { get; set; }
}
