using MediCare.Application.Interfaces.Persistence;

namespace MediCare.Infrastructure.Time;

/// <summary>
/// Clinic clock backed by the injectable TimeProvider. Clinic-local values are
/// treated as UTC because the current application does not configure a clinic
/// time zone; the interface keeps that decision replaceable later.
/// </summary>
public sealed class SystemClinicClock(TimeProvider timeProvider) : IClinicClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public TimeOnly CurrentTime => TimeOnly.FromDateTime(UtcNow);

    public DateTime ToUtc(DateOnly clinicDate, TimeOnly clinicTime) =>
        DateTime.SpecifyKind(clinicDate.ToDateTime(clinicTime), DateTimeKind.Utc);
}
