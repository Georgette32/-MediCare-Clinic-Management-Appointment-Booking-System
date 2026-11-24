using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class CheckInService(
    IAppointmentRepository appointments,
    IQueueRepository queueRepository,
    IExpectedDurationService expectedDurationService,
    IDoctorDisruptionRepository disruptions,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IGracePeriodService gracePeriods,
    IDoctorCallWindowService callWindows,
    ICheckInWarningProvider? warningProvider = null) : ICheckInService
{
    public async Task<ScheduledCheckInResult> CheckInScheduledPatientAsync(Guid appointmentId, Guid expectedDoctorId, CancellationToken cancellationToken)
    {
        var result = await CheckInScheduledCoreAsync(appointmentId, expectedDoctorId, cancellationToken);
        if (result.QueueEntryId is null || warningProvider is null)
            return result;

        // Warning only: the check-in is never blocked by a disruption. The warning is read after the commit.
        var warning = await warningProvider.GetWarningAsync(appointmentId, cancellationToken);
        return warning is null ? result : result with { Warning = warning };
    }

    private Task<ScheduledCheckInResult> CheckInScheduledCoreAsync(Guid appointmentId, Guid expectedDoctorId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var appointment = await appointments.GetByIdAsync(appointmentId, ct)
                ?? throw new InvalidOperationException("The appointment was not found.");

            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new InvalidOperationException("Only confirmed appointments can check in.");

            if (appointment.DoctorId != expectedDoctorId)
                throw new InvalidOperationException("The appointment does not belong to the selected doctor.");

            var today = clinicClock.Today;
            if (appointment.AppointmentDate != today)
                throw new InvalidOperationException("Check-in is permitted only on the day of the appointment.");

            if (await queueRepository.HasLiveQueueEntryForAppointmentAsync(appointmentId, ct))
                throw new InvalidOperationException("The patient has already checked in for this appointment.");

            var now = clinicClock.UtcNow;
            var scheduledStartUtc = clinicClock.ToUtc(appointment.AppointmentDate, appointment.StartTime);
            var hasOpenDisruptionItem = await disruptions.GetOpenItemForAppointmentAsync(appointmentId, ct) is not null;

            // Early arrivals retain scheduled priority. The grace clock begins at the scheduled start,
            // and the check-in itself must enforce expiry even if the background processor has not run.
            // A patient on the disruption worklist is never No-Show'd here; the clinic-unavailable path decides instead.
            if (now >= scheduledStartUtc && !hasOpenDisruptionItem)
            {
                if (appointment.FrozenGracePeriodMinutes is null)
                {
                    var duration = await expectedDurationService.CalculateExpectedDurationMinutesAsync(appointment.DoctorId, today, ct);
                    appointment.StartGracePeriod(duration.DurationMinutes * ExpectedDurationOptions.GracePeriodMultiplier, scheduledStartUtc);
                }

                if (appointment.IsGracePeriodExpired(now))
                {
                    appointment.MarkNoShow(now);
                    return new ScheduledCheckInResult(
                        QueueEntryId: null,
                        RequiresWalkInRegistration: true,
                        Message: "The grace period has expired and the appointment is now marked No-Show. Register the patient as a walk-in.");
                }
            }

            var entry = QueueEntry.CreateScheduled(
                appointment.PatientId,
                appointment.DoctorId,
                appointment.Id,
                today,
                now);

            await queueRepository.AddAsync(entry, ct);
            return new ScheduledCheckInResult(entry.Id, RequiresWalkInRegistration: false, Message: null);
        }, cancellationToken);

    public async Task<Guid> RegisterWalkInAsync(Guid patientId, Guid doctorId, CancellationToken cancellationToken)
    {
        var queueDate = clinicClock.Today;

        // Grace processing runs first, in its own transactions, so a patient whose grace period has just expired is
        // converted to No-Show and can be registered as a walk-in immediately.
        await gracePeriods.ProcessDoctorDayAsync(doctorId, queueDate, cancellationToken);

        return await unitOfWork.ExecuteWalkInRegistrationWithRetryAsync(doctorId, queueDate, async ct =>
        {
            if (!await appointments.PatientExistsAsync(patientId, ct))
                throw new InvalidOperationException("The patient was not found.");

            var doctor = await appointments.GetDoctorAsync(doctorId, ct)
                ?? throw new InvalidOperationException("The active doctor was not found.");

            var today = queueDate;

            var leftEarly = await callWindows.GetLeftEarlyReasonAsync(doctorId, today, clinicClock.CurrentTime, ct);
            if (leftEarly is not null)
                throw new InvalidOperationException(leftEarly);

            if (await appointments.HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(patientId, doctorId, today, ct))
                throw new InvalidOperationException("The patient has a confirmed appointment with this doctor today. Use scheduled check-in.");

            if (await queueRepository.HasActiveEntryForPatientAsync(patientId, doctorId, today, ct))
                throw new InvalidOperationException("The patient is already in the queue for this doctor today.");

            var nextSequence = await queueRepository.GetNextWalkInSequenceAsync(doctor.Id, today, ct);
            var entry = QueueEntry.CreateWalkIn(patientId, doctorId, today, clinicClock.UtcNow, nextSequence);
            await queueRepository.AddAsync(entry, ct);
            return entry.Id;
        }, cancellationToken);
    }
}
