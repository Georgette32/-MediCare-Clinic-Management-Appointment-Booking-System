using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class ReceptionQueueService(
    IQueueRepository queueRepository,
    IAppointmentRepository appointments,
    IWalkInAdmissionApprovalRepository approvals,
    IQueueBoardRepository boardRepository,
    IGracePeriodService gracePeriodService,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IDoctorAvailabilityService availability,
    IDisruptionService disruptions,
    IDoctorCallWindowService callWindows) : IReceptionQueueService
{
    public async Task<string> CallScheduledAsync(Guid queueEntryId, CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.QueueDate, async ct =>
        {
            var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                ?? throw new InvalidOperationException("The queue entry was not found.");

            var today = clinicClock.Today;
            if (entry.Source != QueueEntrySource.Scheduled)
                throw new InvalidOperationException("Walk-ins are called through their admission approval.");
            if (entry.Status != QueueEntryStatus.Waiting)
                throw new InvalidOperationException("Only a Waiting patient can be called.");
            if (entry.QueueDate != today)
                throw new InvalidOperationException("Only today's queue entries can be called.");
            if (!IsCallableScheduled(entry))
                throw new InvalidOperationException("The appointment is no longer confirmed, so the patient cannot be called.");

            var blockReason = await callWindows.GetCallBlockReasonAsync(entry.DoctorId, today, clinicClock.CurrentTime, ct);
            if (blockReason is not null)
                throw new InvalidOperationException(blockReason);

            var queue = await queueRepository.GetActiveEntriesForDoctorAndDateAsync(entry.DoctorId, today, ct);
            if (queue.Any(other => other.Id != entry.Id && other.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation))
                throw new InvalidOperationException("Doctor is busy: another patient is Called or in consultation.");

            var earlier = queue
                .Where(other => other.Id != entry.Id && IsCallableScheduled(other) &&
                                other.Appointment!.StartTime < entry.Appointment!.StartTime)
                .OrderBy(other => other.Appointment!.StartTime)
                .FirstOrDefault();
            if (earlier is not null)
                throw new InvalidOperationException(
                    $"An earlier scheduled patient is waiting ({earlier.Patient.FullName}, {earlier.Appointment!.StartTime:h\\:mm tt}). Call that patient first.");

            entry.Call(clinicClock.UtcNow);
            return "Scheduled patient called.";
        }, cancellationToken);
    }

    public async Task<CallNextResult> CallNextAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;

        // Unresolved disruption items whose appointment time has passed are cancelled first, so they cannot be called.
        // Grace-period processing uses its own serializable transactions, so it must run before the doctor-queue
        // transaction opens. Both are evaluated at operation time, so an expired grace period cannot be bypassed.
        await disruptions.ProcessUnresolvedAsync(clinicClock.UtcNow, cancellationToken);
        await gracePeriodService.ProcessDoctorDayAsync(doctorId, today, cancellationToken);

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(doctorId, today, async ct =>
        {
            var now = clinicClock.UtcNow;
            var queue = await queueRepository.GetActiveEntriesForDoctorAndDateAsync(doctorId, today, ct);

            if (queue.Any(entry => entry.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation))
                return new CallNextResult(false, null, "Doctor is busy: another patient is Called or in consultation.");

            var blockReason = await callWindows.GetCallBlockReasonAsync(doctorId, today, clinicClock.CurrentTime, ct);
            if (blockReason is not null)
                return new CallNextResult(false, null, blockReason);

            var nextScheduled = queue
                .Where(entry => entry.Source == QueueEntrySource.Scheduled && IsCallableScheduled(entry))
                .OrderBy(entry => entry.Appointment!.StartTime)
                .ThenBy(entry => entry.ArrivedAt)
                .FirstOrDefault();
            if (nextScheduled is not null)
            {
                nextScheduled.Call(now);
                return new CallNextResult(true, nextScheduled.Id,
                    $"Scheduled patient {nextScheduled.Patient.FullName} ({nextScheduled.Appointment!.StartTime:h\\:mm tt}) called.");
            }

            // Walk-ins: the earliest FIFO walk-in that holds a usable approval. Walk-ins without one keep their position
            // and do not block approved walk-ins behind them.
            var waitingWalkIns = queue
                .Where(entry => entry.Source == QueueEntrySource.WalkIn && entry.Status == QueueEntryStatus.Waiting)
                .OrderBy(entry => entry.ArrivedAt)
                .ThenBy(entry => entry.QueueSequence)
                .ToArray();
            if (waitingWalkIns.Length == 0)
                return new CallNextResult(false, null, "No patient is waiting for this doctor.");

            var approvalRecords = await approvals.GetForQueueEntriesAsync(
                queue.Where(entry => entry.Source == QueueEntrySource.WalkIn).Select(entry => entry.Id).ToArray(), ct);
            var selected = waitingWalkIns
                .Select(entry => (Entry: entry, Approval: approvalRecords
                    .FirstOrDefault(approval => WalkInCallRules.IsUsableApproval(approval, entry, now, today))))
                .FirstOrDefault(pair => pair.Approval is not null);
            if (selected.Entry is null)
                return new CallNextResult(false, null,
                    $"No approved walk-in is waiting; {waitingWalkIns.Length} walk-ins need the doctor's approval.");

            var walkIn = selected.Entry;
            var headLabel = $"#{walkIn.QueueSequence}, {walkIn.Patient.FullName}";
            var doctor = await appointments.GetDoctorAsync(doctorId, ct);
            var workingHours = await availability.GetEffectiveWindowsAsync(doctorId, today, ct);
            var context = new WalkInCallContext(today, now, clinicClock.CurrentTime, doctor is not null, queue, workingHours,
                approvalRecords.ToArray());

            try
            {
                // Same validation as the explicit walk-in Call operation.
                WalkInCallRules.ValidateAndCall(walkIn, selected.Approval!, context);
            }
            catch (InvalidOperationException exception)
            {
                return new CallNextResult(false, null, $"Next walk-in ({headLabel}) cannot be called: {exception.Message}");
            }

            return new CallNextResult(true, walkIn.Id, $"Walk-in ({headLabel}) called.");
        }, cancellationToken);
    }

    public async Task<string> ReturnToWaitingAsync(Guid queueEntryId, CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.QueueDate, async ct =>
        {
            var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                ?? throw new InvalidOperationException("The queue entry was not found.");

            entry.ReturnToWaiting();
            return entry.Source == QueueEntrySource.WalkIn
                ? "Patient returned to the waiting list. The walk-in's admission approval was already used, so a new approval is needed before calling this patient."
                : "Patient returned to the waiting list.";
        }, cancellationToken);
    }

    public async Task<string> CancelQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.QueueDate, async ct =>
        {
            var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                ?? throw new InvalidOperationException("The queue entry was not found.");

            entry.Cancel();

            // A Pending admission request for a cancelled entry can no longer be used; record it as Expired now.
            var now = clinicClock.UtcNow;
            foreach (var approval in await approvals.GetForQueueEntriesAsync(new[] { entry.Id }, ct))
            {
                if (approval.Decision == WalkInAdmissionDecision.Pending)
                    approval.WithdrawPending(now);
            }

            return entry.AppointmentId is not null
                ? "Queue entry cancelled. The appointment remains Confirmed, so the patient may check in again later."
                : "Queue entry cancelled.";
        }, cancellationToken);
    }

    public async Task<PatientLookupResult> FindPatientByPhoneAsync(string input, CancellationToken cancellationToken)
    {
        if (!EgyptianMobileNumber.TryNormalize(input, out var canonical, out var error))
            return new PatientLookupResult(null, error, NotRegistered: false);

        var patientId = await boardRepository.FindPatientIdByPhoneAsync(canonical, cancellationToken);
        return patientId is null
            ? new PatientLookupResult(null, "No patient account has this phone number.", NotRegistered: true)
            : new PatientLookupResult(patientId, null, NotRegistered: false);
    }

    /// <summary>
    /// A scheduled entry is callable only while its appointment is still Confirmed. Explicit Call and Call Next share this rule.
    /// </summary>
    private static bool IsCallableScheduled(QueueEntry entry) =>
        entry.Source == QueueEntrySource.Scheduled &&
        entry.Status == QueueEntryStatus.Waiting &&
        entry.Appointment is { Status: AppointmentStatus.Confirmed };
}
