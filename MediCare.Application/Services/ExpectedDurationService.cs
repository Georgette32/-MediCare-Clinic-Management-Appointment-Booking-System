using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using Microsoft.Extensions.Options;

namespace MediCare.Application.Services;

public sealed class ExpectedDurationService(
    IAppointmentRepository appointments,
    IQueueRepository queueRepository,
    IOptions<ExpectedDurationOptions> options,
    IClinicSettingsProvider clinicSettings) : IExpectedDurationService
{
    private readonly ExpectedDurationOptions _options = options.Value;

    public async Task<ExpectedDurationResult> CalculateExpectedDurationMinutesAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorAsync(doctorId, cancellationToken)
            ?? throw new InvalidOperationException("The active doctor was not found.");

        var fromDate = clinicDate.AddDays(-ExpectedDurationOptions.TrailingDaysWindow);
        var toDate = clinicDate.AddDays(-1);

        var completedVisits = await queueRepository.GetCompletedVisitsAsync(doctorId, fromDate, toDate, cancellationToken);

        var validDurations = completedVisits
            .Select(v => (v.CompletedAt!.Value - v.ConsultationStartedAt!.Value).TotalMinutes)
            .Where(m => m > 0 && m < _options.MaxDurationMinutesCap)
            .ToArray();

        var minSample = await clinicSettings.GetMinimumConsultationSampleSizeAsync(cancellationToken);
        var hasSufficientSample = minSample.HasValue && minSample.Value > 0 && validDurations.Length >= minSample.Value;

        if (!hasSufficientSample || validDurations.Length == 0)
        {
            return new ExpectedDurationResult(
                doctor.AppointmentDurationMinutes,
                UsedHistoricalAverage: false,
                ValidSampleCount: validDurations.Length,
                RawAverageMinutes: validDurations.Length > 0 ? validDurations.Average() : null);
        }

        var average = validDurations.Average();
        var roundedMinutes = (int)Math.Round(average, MidpointRounding.AwayFromZero);
        if (roundedMinutes <= 0)
            roundedMinutes = 1;

        return new ExpectedDurationResult(
            roundedMinutes,
            UsedHistoricalAverage: true,
            ValidSampleCount: validDurations.Length,
            RawAverageMinutes: average);
    }
}
