namespace MediCare.Application.Abstractions;

public interface IClinicClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
    TimeOnly CurrentTime { get; }
    DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime);
}
