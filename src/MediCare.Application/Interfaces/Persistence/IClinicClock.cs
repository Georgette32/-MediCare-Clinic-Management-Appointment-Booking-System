namespace MediCare.Application.Interfaces.Persistence;

/// <summary>
/// Supplies the current clinic time. Kept behind an interface so scheduling rules
/// can be tested without waiting for real time to pass.
/// </summary>
public interface IClinicClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }

    TimeOnly CurrentTime { get; }

    DateTime ToUtc(DateOnly clinicDate, TimeOnly clinicTime);
}
