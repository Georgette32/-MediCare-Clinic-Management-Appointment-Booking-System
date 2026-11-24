using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class GracePeriodService(
    IAppointmentRepository appointments,
    IQueueRepository queueRepository,
    IExpectedDurationService expectedDurationService,
    IDoctorDisruptionRepository disruptions,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : IGracePeriodService
{
    public async Task<GracePeriodStartResult> StartGracePeriodIfEligibleAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var appointment = await appointments.GetByIdAsync(appointmentId, cancellationToken)
            ?? throw new InvalidOperationException("The appointment was not found.");

        if (appointment.Status != AppointmentStatus.Confirmed)
            throw new InvalidOperationException("Grace period can only be started for confirmed appointments.");

        var today = clinicClock.Today;
        if (appointment.AppointmentDate != today)
            throw new InvalidOperationException("Grace period can only be started on the day of the appointment.");

        if (await queueRepository.HasLiveQueueEntryForAppointmentAsync(appointmentId, cancellationToken))
            throw new InvalidOperationException("Cannot start grace period because the patient has already checked in.");

        if (appointment.FrozenGracePeriodMinutes.HasValue)
        {
            return new GracePeriodStartResult(
                appointment.Id,
                appointment.FrozenGracePeriodMinutes.Value,
                appointment.GracePeriodStartedAtUtc!.Value,
                StartedSuccessfully: false);
        }

        // An unresolved disruption item is handled by staff or by the clinic-unavailable path, never by Grace Period.
        if (await disruptions.GetOpenItemForAppointmentAsync(appointmentId, cancellationToken) is not null)
            return new GracePeriodStartResult(appointment.Id, FrozenGracePeriodMinutes: 0, StartedAtUtc: clinicClock.UtcNow, StartedSuccessfully: false);

        var now = clinicClock.UtcNow;
        var scheduledStartUtc = clinicClock.ToUtc(appointment.AppointmentDate, appointment.StartTime);
        if (now < scheduledStartUtc)
        {
            return new GracePeriodStartResult(
                appointment.Id,
                FrozenGracePeriodMinutes: 0,
                StartedAtUtc: now,
                StartedSuccessfully: false);
        }

        var durationResult = await expectedDurationService.CalculateExpectedDurationMinutesAsync(appointment.DoctorId, today, cancellationToken);
        var gracePeriodMinutes = durationResult.DurationMinutes * ExpectedDurationOptions.GracePeriodMultiplier;

        appointment.StartGracePeriod(gracePeriodMinutes, scheduledStartUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new GracePeriodStartResult(
            appointment.Id,
            gracePeriodMinutes,
            scheduledStartUtc,
            StartedSuccessfully: true);
    }

    public async Task<int> ProcessExpiredGracePeriodsForDoctorAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken)
    {
        if (clinicDate != clinicClock.Today)
            throw new ArgumentException("Grace-period expiry can only be processed for the current clinic date.", nameof(clinicDate));

        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var confirmedAppointments = await appointments.GetConfirmedForDoctorAndDateAsync(doctorId, clinicDate, cancellationToken);
        var now = clinicClock.UtcNow;
        var markedCount = 0;

        foreach (var appointment in confirmedAppointments)
        {
            if (appointment.FrozenGracePeriodMinutes is null || appointment.GracePeriodStartedAtUtc is null)
                continue;

            if (await queueRepository.HasLiveQueueEntryForAppointmentAsync(appointment.Id, cancellationToken))
                continue;

            // Unresolved disruption items go through the clinic-unavailable path, not No-Show.
            if (await disruptions.GetOpenItemForAppointmentAsync(appointment.Id, cancellationToken) is not null)
                continue;

            if (appointment.IsGracePeriodExpired(now))
            {
                appointment.MarkNoShow(now);
                markedCount++;
            }
        }

        if (markedCount > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return markedCount;
    }

    public async Task<int> ProcessDoctorDayAsync(Guid doctorId, DateOnly clinicDate, CancellationToken cancellationToken)
    {
        if (clinicDate != clinicClock.Today)
            throw new ArgumentException("Grace-period processing can only run for the current clinic date.", nameof(clinicDate));

        var now = clinicClock.UtcNow;
        var started = 0;
        foreach (var appointment in await appointments.GetConfirmedForDoctorAndDateAsync(doctorId, clinicDate, cancellationToken))
        {
            if (appointment.FrozenGracePeriodMinutes is not null)
                continue;
            if (clinicClock.ToUtc(clinicDate, appointment.StartTime) > now)
                continue;
            if (await queueRepository.HasLiveQueueEntryForAppointmentAsync(appointment.Id, cancellationToken))
                continue;

            var result = await StartGracePeriodIfEligibleAsync(appointment.Id, cancellationToken);
            if (result.StartedSuccessfully)
                started++;
        }

        return started + await ProcessExpiredGracePeriodsForDoctorAsync(doctorId, clinicDate, cancellationToken);
    }
}
