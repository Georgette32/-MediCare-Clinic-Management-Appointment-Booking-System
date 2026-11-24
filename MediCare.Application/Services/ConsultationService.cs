using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class ConsultationService(
    IQueueRepository queueRepository,
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : IConsultationService
{
    public Task<ConsultationActionResult> StartConsultationAsync(Guid queueEntryId, Guid doctorApplicationUserId, CancellationToken cancellationToken) =>
        RunForOwnDoctorAsync(queueEntryId, doctorApplicationUserId, cancellationToken, async (entry, ct) =>
        {
            if (entry.Status != QueueEntryStatus.Called)
                return Fail($"Only a Called patient can start a consultation (current status: {entry.Status}).");

            var queue = await queueRepository.GetActiveEntriesForDoctorAndDateAsync(entry.DoctorId, entry.QueueDate, ct);
            if (queue.Any(other => other.Id != entry.Id && other.Status == QueueEntryStatus.InConsultation))
                return Fail("Another consultation is already in progress for you. Complete it first.");

            try
            {
                entry.StartConsultation(clinicClock.UtcNow);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
            {
                // The domain checks run before any mutation, so nothing is left half-changed.
                return Fail(exception.Message);
            }

            return new ConsultationActionResult(true, "Consultation started.");
        });

    public Task<ConsultationActionResult> CompleteConsultationAsync(Guid queueEntryId, Guid doctorApplicationUserId, CancellationToken cancellationToken) =>
        RunForOwnDoctorAsync(queueEntryId, doctorApplicationUserId, cancellationToken, (entry, ct) =>
        {
            if (entry.Status != QueueEntryStatus.InConsultation)
                return Task.FromResult(Fail($"Only an in-consultation patient can be completed (current status: {entry.Status})."));

            var now = clinicClock.UtcNow;
            if (entry.ConsultationStartedAt is null || now <= entry.ConsultationStartedAt)
                return Task.FromResult(Fail("The consultation end time must follow its start."));

            // Check the appointment precondition before mutating anything: the transaction commits whatever is
            // changed when the operation returns normally, so a failure must be detected before the first change.
            var appointment = entry.Appointment;
            if (entry.Source == QueueEntrySource.Scheduled && appointment is null)
                return Task.FromResult(Fail("The scheduled appointment for this consultation could not be found."));
            if (entry.Source == QueueEntrySource.Scheduled && appointment!.Status != AppointmentStatus.Confirmed)
                return Task.FromResult(Fail($"The appointment is {appointment.Status}, so it cannot be completed."));

            entry.Complete(now);
            if (entry.Source == QueueEntrySource.Scheduled)
                appointment!.MarkCompleted(now);

            return Task.FromResult(new ConsultationActionResult(true, "Consultation completed."));
        });

    private static ConsultationActionResult Fail(string message) => new(false, message);

    /// <summary>
    /// Shared flow: resolves the caller's doctor profile, verifies that the queue entry belongs to it (a failure
    /// result, never an exception leak), then runs the change under the doctor-queue lock with state reloaded inside.
    /// </summary>
    private async Task<ConsultationActionResult> RunForOwnDoctorAsync(
        Guid queueEntryId,
        Guid doctorApplicationUserId,
        CancellationToken cancellationToken,
        Func<QueueEntry, CancellationToken, Task<ConsultationActionResult>> action)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorApplicationUserId, cancellationToken);
        if (doctor is null)
            return Fail("Your account is not linked to an active doctor profile.");

        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken);
        if (preview is null || preview.DoctorId != doctor.Id)
            return Fail("The queue entry was not found in your queue.");

        try
        {
            return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(doctor.Id, preview.QueueDate, async ct =>
            {
                var entry = await queueRepository.GetByIdAsync(queueEntryId, ct);
                if (entry is null || entry.DoctorId != doctor.Id)
                    return Fail("The queue entry was not found in your queue.");

                return await action(entry, ct);
            }, cancellationToken);
        }
        catch (UniqueConstraintConflictException exception)
        {
            // Another live consultation won the one-active-consultation index between the check and the save.
            return Fail(exception.Message);
        }
    }
}

public interface IConsultationService
{
    Task<ConsultationActionResult> StartConsultationAsync(Guid queueEntryId, Guid doctorApplicationUserId, CancellationToken cancellationToken);
    Task<ConsultationActionResult> CompleteConsultationAsync(Guid queueEntryId, Guid doctorApplicationUserId, CancellationToken cancellationToken);
}

public sealed record ConsultationActionResult(bool Succeeded, string Message);
