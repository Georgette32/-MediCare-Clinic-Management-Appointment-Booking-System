using MediCare.Application.Common;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace MediCare.Application.Services;

public sealed class ExpectedDurationService(
    IAppointmentRepository appointments,
    IQueueRepository queueRepository,
    IOptions<ExpectedDurationOptions> options) : IExpectedDurationService
{
    private readonly ExpectedDurationOptions _options = options.Value;

    public async Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(
        Guid doctorProfileId,
        DateOnly clinicDate,
        CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorProfileAsync(doctorProfileId, cancellationToken)
            ?? throw new InvalidOperationException("The active doctor was not found.");

        var fromDate = clinicDate.AddDays(-ExpectedDurationOptions.TrailingDaysWindow);
        var toDate = clinicDate.AddDays(-1);

        var completedVisits = await queueRepository.GetCompletedVisitsAsync(
            doctorProfileId, fromDate, toDate, cancellationToken);

        var validDurations = completedVisits
            .Select(visit => (visit.CompletedAt!.Value - visit.ConsultationStartedAt!.Value).TotalMinutes)
            .Where(minutes => minutes > 0 && minutes < _options.MaxDurationMinutesCap)
            .ToArray();

        var minimumSample = _options.MinimumSampleSize;
        var hasSufficientSample = minimumSample.HasValue &&
                                  minimumSample.Value > 0 &&
                                  validDurations.Length >= minimumSample.Value;

        if (!hasSufficientSample || validDurations.Length == 0)
        {
            return new ExpectedDurationResult(
                doctor.AppointmentDurationMinutes,
                UsedHistoricalAverage: false,
                validDurations.Length,
                validDurations.Length > 0 ? validDurations.Average() : null);
        }

        var average = validDurations.Average();
        var roundedMinutes = (int)Math.Round(average, MidpointRounding.AwayFromZero);
        if (roundedMinutes <= 0)
        {
            roundedMinutes = 1;
        }

        return new ExpectedDurationResult(
            roundedMinutes,
            UsedHistoricalAverage: true,
            validDurations.Length,
            average);
    }
}
