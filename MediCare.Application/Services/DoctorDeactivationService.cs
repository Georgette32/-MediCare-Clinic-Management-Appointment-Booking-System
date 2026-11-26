using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>What deactivating a doctor would change. Read-only; the Admin confirm page shows these counts.</summary>
public sealed record DoctorDeactivationPreview(string DoctorName, int AppointmentsToCancel, int WaitingEntriesToCancel, string? BlockReason);

/// <summary>
/// Deactivates a doctor in one serializable operation: future Pending/Confirmed appointments are cancelled with the reason
/// "doctor no longer available", Waiting queue entries are cancelled, and the account is deactivated. Patients are notified
/// after the commit. A doctor with a patient Called or InConsultation cannot be deactivated.
/// </summary>
public interface IDoctorDeactivationService
{
    Task<DoctorDeactivationPreview> PreviewAsync(Guid doctorUserId, CancellationToken cancellationToken);
    Task<ManagementResult> DeactivateAsync(Guid doctorUserId, CancellationToken cancellationToken);
}

public sealed class DoctorDeactivationService(
    IAppointmentRepository appointments,
    IQueueRepository queue,
    IWalkInAdmissionApprovalRepository approvals,
    IStaffAccountRepository staff,
    AppointmentService appointmentService,
    IPatientNotificationPublisher publisher,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : IDoctorDeactivationService
{
    public async Task<DoctorDeactivationPreview> PreviewAsync(Guid doctorUserId, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
            return new DoctorDeactivationPreview(string.Empty, 0, 0, "No active doctor profile is linked to this account.");

        var today = clinicClock.Today;
        var nowTime = clinicClock.CurrentTime;
        var queueToday = await queue.GetActiveEntriesForDoctorAndDateAsync(doctor.Id, today, cancellationToken);
        var block = BlockReason(doctor, queueToday);
        var future = (await appointments.GetLiveForDoctorFromDateAsync(doctor.Id, today, cancellationToken))
            .Count(item => IsFutureForDeactivation(item, today, nowTime));
        var waiting = queueToday.Count(entry => entry.Status == QueueEntryStatus.Waiting);
        return new DoctorDeactivationPreview(doctor.FullName, future, waiting, block);
    }

    public async Task<ManagementResult> DeactivateAsync(Guid doctorUserId, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await unitOfWork.ExecuteSerializableWithRetryAsync(
                ct => DeactivateCoreAsync(doctorUserId, ct), cancellationToken);

            foreach (var notification in outcome.Notifications)
                await publisher.PublishAsync(notification, cancellationToken);

            return ManagementResult.Success(
                $"Doctor deactivated. {outcome.AppointmentsCancelled} appointment(s) cancelled and {outcome.Notifications.Count} patient(s) notified.");
        }
        catch (InvalidOperationException exception)
        {
            return ManagementResult.Failure(exception.Message);
        }
    }

    private async Task<DeactivationOutcome> DeactivateCoreAsync(Guid doctorUserId, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            // No active profile to clean up: only the account flag changes.
            await staff.SetAccountActiveAsync(doctorUserId, false, cancellationToken);
            return new DeactivationOutcome(0, []);
        }

        var today = clinicClock.Today;
        var nowTime = clinicClock.CurrentTime;
        var now = clinicClock.UtcNow;
        var queueToday = await queue.GetActiveEntriesForDoctorAndDateAsync(doctor.Id, today, cancellationToken);
        var block = BlockReason(doctor, queueToday);
        if (block is not null)
            throw new InvalidOperationException(block);

        var notifications = new List<PatientNotification>();
        var cancelledAppointments = 0;
        var futureAppointments = (await appointments.GetLiveForDoctorFromDateAsync(doctor.Id, today, cancellationToken))
            .Where(item => IsFutureForDeactivation(item, today, nowTime))
            .ToArray();

        foreach (var appointment in futureAppointments)
        {
            // A live replacement may already have been cancelled with its original earlier in this loop.
            var current = await appointments.GetByIdAsync(appointment.Id, cancellationToken);
            if (current is null || !AppointmentPolicy.IsLive(current.Status))
                continue;

            await appointmentService.CancelByClinicCoreAsync(appointment.Id, AppointmentCancellationReasons.DoctorNoLongerAvailable, cancellationToken);
            cancelledAppointments++;
            notifications.Add(new PatientNotification(
                appointment.PatientId,
                PatientNotificationKind.AppointmentCancelledDoctorNoLongerAvailable,
                "Appointment cancelled",
                $"Your appointment on {appointment.AppointmentDate:yyyy-MM-dd} at {appointment.StartTime:h\\:mm tt} cannot take place because the doctor is no longer available. Please contact the clinic to rebook.",
                appointment.Id));
        }

        foreach (var entry in queueToday)
        {
            // Entries released with their appointment above are no longer Waiting here.
            if (entry.Status != QueueEntryStatus.Waiting)
                continue;

            entry.Cancel();
            foreach (var approval in await approvals.GetForQueueEntriesAsync(new[] { entry.Id }, cancellationToken))
            {
                if (approval.Decision == WalkInAdmissionDecision.Pending)
                    approval.WithdrawPending(now);
            }

            notifications.Add(entry.Source == QueueEntrySource.WalkIn
                ? new PatientNotification(
                    entry.PatientId,
                    PatientNotificationKind.WalkInCancelledDoctorNoLongerAvailable,
                    "Visit cancelled",
                    "Your clinic visit cannot take place because the doctor is no longer available. Please contact the clinic.",
                    RelatedAppointmentId: null)
                : new PatientNotification(
                    entry.PatientId,
                    PatientNotificationKind.AppointmentCancelledDoctorNoLongerAvailable,
                    "Visit cancelled",
                    "Your check-in for today cannot take place because the doctor is no longer available. Please contact the clinic.",
                    entry.AppointmentId));
        }

        await staff.SetAccountActiveAsync(doctorUserId, false, cancellationToken);
        return new DeactivationOutcome(cancelledAppointments, notifications);
    }

    /// <summary>Future means a later date, or today with a start time that has not yet passed.</summary>
    private static bool IsFutureForDeactivation(Appointment appointment, DateOnly today, TimeOnly nowTime) =>
        appointment.AppointmentDate > today || (appointment.AppointmentDate == today && appointment.StartTime >= nowTime);

    private static string? BlockReason(Doctor doctor, IReadOnlyList<QueueEntry> queueToday)
    {
        var busy = queueToday.Any(entry => entry.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation);
        return busy
            ? $"{doctor.FullName} has a patient Called or in consultation. Deactivate after that consultation ends."
            : null;
    }

    private sealed record DeactivationOutcome(int AppointmentsCancelled, IReadOnlyList<PatientNotification> Notifications);
}
