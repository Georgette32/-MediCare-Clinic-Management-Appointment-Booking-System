using MediCare.Application.Common;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class CheckInService(
    IAppointmentRepository appointments,
    IQueueRepository queueRepository,
    IExpectedDurationService expectedDurationService,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : ICheckInService
{
    public Task<ScheduledCheckInResult> CheckInScheduledPatientAsync(
        Guid appointmentId,
        Guid expectedDoctorProfileId,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var appointment = await appointments.GetByIdAsync(appointmentId, ct)
                ?? throw new InvalidOperationException("The appointment was not found.");

            if (appointment.Status != AppointmentStatus.Confirmed)
            {
                throw new InvalidOperationException("Only confirmed appointments can check in.");
            }

            if (appointment.DoctorProfileId != expectedDoctorProfileId)
            {
                throw new InvalidOperationException(
                    "The appointment does not belong to the selected doctor.");
            }

            var today = clinicClock.Today;
            if (appointment.AppointmentDate != today)
            {
                throw new InvalidOperationException(
                    "Check-in is permitted only on the day of the appointment.");
            }

            if (await queueRepository.HasQueueEntryForAppointmentAsync(appointmentId, ct))
            {
                throw new InvalidOperationException(
                    "The patient has already checked in for this appointment.");
            }

            var now = clinicClock.UtcNow;
            var scheduledStartUtc = clinicClock.ToUtc(appointment.AppointmentDate, appointment.StartTime);

            // Early arrivals keep their scheduled priority. Once the slot starts, the
            // grace clock begins and check-in must enforce expiry even if the background
            // processor has not run yet.
            if (now >= scheduledStartUtc)
            {
                if (appointment.FrozenGracePeriodMinutes is null)
                {
                    var duration = await expectedDurationService.CalculateExpectedDurationMinutesAsync(
                        appointment.DoctorProfileId, today, ct);

                    appointment.StartGracePeriod(
                        duration.DurationMinutes * ExpectedDurationOptions.GracePeriodMultiplier,
                        scheduledStartUtc);
                }

                if (appointment.IsGracePeriodExpired(now))
                {
                    appointment.MarkNoShow(now);
                    return new ScheduledCheckInResult(
                        null,
                        RequiresWalkInRegistration: true,
                        "The grace period has expired and the appointment is now marked No-Show. Register the patient as a walk-in.");
                }
            }

            var entry = QueueEntry.CreateScheduled(
                appointment.PatientProfileId,
                appointment.DoctorProfileId,
                appointment.Id,
                today,
                now);

            await queueRepository.AddAsync(entry, ct);
            return new ScheduledCheckInResult(entry.Id, false, null);
        }, cancellationToken);

    public Task<Guid> RegisterWalkInAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        CancellationToken cancellationToken)
    {
        var queueDate = clinicClock.Today;

        return unitOfWork.ExecuteWalkInRegistrationWithRetryAsync(
            doctorProfileId,
            queueDate,
            async ct =>
            {
                if (!await appointments.PatientExistsAsync(patientProfileId, ct))
                {
                    throw new InvalidOperationException("The patient was not found.");
                }

                var doctor = await appointments.GetDoctorProfileAsync(doctorProfileId, ct)
                    ?? throw new InvalidOperationException("The active doctor was not found.");

                if (await appointments.HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(
                        patientProfileId, doctor.Id, queueDate, ct))
                {
                    throw new InvalidOperationException(
                        "The patient has a confirmed appointment with this doctor today. Use scheduled check-in.");
                }

                if (await queueRepository.HasActiveEntryForPatientAsync(
                        patientProfileId, doctor.Id, queueDate, ct))
                {
                    throw new InvalidOperationException(
                        "The patient is already in the queue for this doctor today.");
                }

                var nextSequence = await queueRepository.GetNextWalkInSequenceAsync(doctor.Id, queueDate, ct);
                var entry = QueueEntry.CreateWalkIn(
                    patientProfileId, doctor.Id, queueDate, clinicClock.UtcNow, nextSequence);

                await queueRepository.AddAsync(entry, ct);
                return entry.Id;
            },
            cancellationToken);
    }
}
