namespace MediCare.Application.Services;

public interface IExpectedDurationService
{
    Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken);
}

public sealed record ExpectedDurationResult(
    int DurationMinutes,
    bool UsedHistoricalAverage,
    int ValidSampleCount,
    double? RawAverageMinutes);
