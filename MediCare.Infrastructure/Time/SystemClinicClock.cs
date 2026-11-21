using MediCare.Application.Abstractions;

namespace MediCare.Infrastructure.Time;

public sealed class SystemClinicClock(TimeProvider timeProvider, TimeZoneInfo clinicTimeZone) : IClinicClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow().ToUniversalTime();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, clinicTimeZone).DateTime);
}

public static class ClinicTimeZoneResolver
{
    public static TimeZoneInfo Resolve(string configuredId)
    {
        if (string.IsNullOrWhiteSpace(configuredId))
            throw new InvalidOperationException("Clinic:TimeZoneId must be configured.");

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configuredId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(configuredId, out var windowsId) && windowsId is not null)
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);

            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(configuredId, out var ianaId) && ianaId is not null)
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);

            throw new InvalidOperationException($"Clinic timezone '{configuredId}' is not available on this host.");
        }
    }
}
