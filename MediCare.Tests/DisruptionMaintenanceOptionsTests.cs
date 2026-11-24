using System.ComponentModel.DataAnnotations;
using MediCare.Infrastructure.Maintenance;

namespace MediCare.Tests;

/// <summary>The maintenance interval has no code default: it must be configured and positive before the host starts.</summary>
public sealed class DisruptionMaintenanceOptionsTests
{
    [Fact]
    public void Unconfigured_or_non_positive_interval_fails_validation()
    {
        Assert.False(Validate(new DisruptionMaintenanceOptions { IntervalMinutes = 0 }));
        Assert.False(Validate(new DisruptionMaintenanceOptions { IntervalMinutes = -5 }));
        Assert.False(Validate(new DisruptionMaintenanceOptions()));
    }

    [Fact]
    public void Positive_interval_passes_validation()
    {
        Assert.True(Validate(new DisruptionMaintenanceOptions { IntervalMinutes = 5 }));
    }

    private static bool Validate(DisruptionMaintenanceOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), validationResults: null, validateAllProperties: true);
}
