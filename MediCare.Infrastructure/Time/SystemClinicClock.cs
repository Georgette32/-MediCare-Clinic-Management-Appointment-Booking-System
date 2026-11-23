using MediCare.Application.Abstractions;

namespace MediCare.Infrastructure.Time;

public sealed class SystemClinicClock(TimeProvider timeProvider, TimeZoneInfo clinicTimeZone) : IClinicClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow().ToUniversalTime();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, clinicTimeZone).DateTime);

    public TimeOnly CurrentTime => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, clinicTimeZone).DateTime);

    public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime)
    {
        var localDateTime = DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Unspecified);
        if (clinicTimeZone.IsInvalidTime(localDateTime))
            localDateTime = ResolveFirstValidTimeAfterGap(localDateTime);

        // ConvertTimeToUtc resolves ambiguous fall-back times using standard time.
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDateTime, clinicTimeZone));
    }

    private DateTime ResolveFirstValidTimeAfterGap(DateTime invalidLocalDateTime)
    {
        var lowerInvalidTicks = invalidLocalDateTime.Ticks;
        var upperTicks = lowerInvalidTicks;
        var stepTicks = TimeSpan.TicksPerMinute;

        while (upperTicks < DateTime.MaxValue.Ticks)
        {
            var nextTicks = upperTicks + Math.Min(stepTicks, DateTime.MaxValue.Ticks - upperTicks);
            upperTicks = nextTicks;
            if (!clinicTimeZone.IsInvalidTime(new DateTime(upperTicks, DateTimeKind.Unspecified)))
                break;

            lowerInvalidTicks = upperTicks;
            stepTicks = Math.Min(stepTicks * 2, TimeSpan.TicksPerDay);
        }

        if (clinicTimeZone.IsInvalidTime(new DateTime(upperTicks, DateTimeKind.Unspecified)))
            throw new InvalidOperationException("No valid clinic-local instant follows the scheduled time.");

        while (upperTicks - lowerInvalidTicks > 1)
        {
            var middleTicks = lowerInvalidTicks + ((upperTicks - lowerInvalidTicks) / 2);
            if (clinicTimeZone.IsInvalidTime(new DateTime(middleTicks, DateTimeKind.Unspecified)))
                lowerInvalidTicks = middleTicks;
            else
                upperTicks = middleTicks;
        }

        return new DateTime(upperTicks, DateTimeKind.Unspecified);
    }
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
