namespace MediCare.Application.Abstractions;

public interface IClinicClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
}
