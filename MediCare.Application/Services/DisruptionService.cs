using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class DisruptionService(
    IDoctorDisruptionRepository disruptions,
    IAppointmentRepository appointments,
    IQueueRepository queue,
    IWalkInAdmissionApprovalRepository approvals,
    AppointmentService appointmentService,
    IDoctorAvailabilityService availability,
    IPatientNotificationPublisher publisher,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : IDisruptionService
{
    public Task<Guid> RecordAsync(Guid doctorId, DateOnly date, TimeOnly fromTime, string reason, Guid actorUserId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var doctor = await appointments.GetDoctorAsync(doctorId, ct)
                ?? throw new InvalidOperationException("The active doctor was not found.");
            if (date < clinicClock.Today)
                throw new InvalidOperationException("A disruption cannot be recorded for a past date.");
            if (await disruptions.ExistsForDoctorAndDateAsync(doctor.Id, date, ct))
                throw new InvalidOperationException("A disruption is already recorded for this doctor on this date.");

            var now = clinicClock.UtcNow;
            var disruption = DoctorDisruption.Record(doctor.Id, date, fromTime, reason, actorUserId, now);
            await disruptions.AddDisruptionAsync(disruption, ct);
            await disruptions.AddBreaksAsync(await BuildBlockedTimeAsync(doctor.Id, date, fromTime, disruption.Id, ct), ct);
            await disruptions.AddItemsAsync(await BuildWorklistAsync(doctor.Id, date, fromTime, disruption.Id, now, ct), ct);
            return disruption.Id;
        }, cancellationToken);

    public async Task<IReadOnlyList<DisruptionListItem>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await disruptions.GetRecentAsync(take, cancellationToken);
        return rows
            .Select(item => new DisruptionListItem(item.Id, item.Doctor.FullName, item.Date, item.FromTime, item.Reason, item.RecordedAtUtc, item.IsDayEndRecorded))
            .ToArray();
    }

    public async Task<DisruptionWorklist> GetWorklistAsync(Guid disruptionId, CancellationToken cancellationToken)
    {
        var disruption = await disruptions.GetByIdAsync(disruptionId, cancellationToken)
            ?? throw new InvalidOperationException("The disruption was not found.");
        var items = await disruptions.GetItemsForDisruptionAsync(disruptionId, cancellationToken);
        var attempts = await disruptions.GetAttemptsForDisruptionAsync(disruptionId, cancellationToken);

        var userIds = attempts.Select(attempt => attempt.ContactedByUserId)
            .Concat(items.Where(item => item.LastContactedByUserId is not null).Select(item => item.LastContactedByUserId!.Value))
            .Distinct()
            .ToArray();
        var names = userIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await disruptions.GetUserDisplayNamesAsync(userIds, cancellationToken);
        string NameOf(Guid userId) => names.TryGetValue(userId, out var name) ? name : "Staff member";

        var historyByItem = attempts
            .GroupBy(attempt => attempt.WorklistItemId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(attempt => attempt.ContactedAtUtc)
                    .Select(attempt => new ContactAttemptView(attempt.ContactedAtUtc, NameOf(attempt.ContactedByUserId), attempt.Notes, attempt.Outcome))
                    .ToArray());

        var views = items
            .OrderBy(item => item.Appointment is null ? 1 : 0)
            .ThenBy(item => item.Appointment?.StartTime)
            .ThenBy(item => item.QueueEntry?.ArrivedAt)
            .Select(item => new WorklistItemView(
                item.Id,
                item.PatientId,
                item.Patient.FullName,
                item.Patient.PhoneNumber,
                item.AppointmentId,
                item.Appointment?.AppointmentDate,
                item.Appointment?.StartTime,
                item.Appointment?.Status,
                item.QueueEntryId,
                item.QueueEntry?.Status,
                item.Outcome,
                item.IsOpen,
                item.LastContactedAtUtc,
                item.LastContactedByUserId is Guid contactedBy ? NameOf(contactedBy) : null,
                item.Notes,
                historyByItem.TryGetValue(item.Id, out var history) ? history : Array.Empty<ContactAttemptView>()))
            .ToArray();

        return new DisruptionWorklist(
            disruption.Id,
            disruption.Doctor.FullName,
            disruption.Date,
            disruption.FromTime,
            disruption.Reason,
            disruption.RecordedAtUtc,
            disruption.IsDayEndRecorded,
            disruption.DayEndRecordedAtUtc,
            views);
    }

    public async Task LogContactAsync(Guid itemId, Guid actorUserId, string? notes, CancellationToken cancellationToken)
    {
        await InTransactionAsync(async ct =>
        {
            var item = await LoadOpenItemAsync(itemId, ct);
            var attempt = item.LogContact(actorUserId, notes, clinicClock.UtcNow);
            await disruptions.AddContactAttemptAsync(attempt, ct);
            return true;
        }, cancellationToken);
    }

    public async Task<Guid> RescheduleFromWorklistAsync(
        Guid itemId,
        Guid actorUserId,
        Guid newDoctorId,
        DateOnly newDate,
        TimeOnly newStartTime,
        string? notes,
        CancellationToken cancellationToken)
    {
        var (replacementId, notification) = await InTransactionAsync(async ct =>
        {
            var item = await LoadOpenItemAsync(itemId, ct);
            if (item.AppointmentId is not Guid appointmentId)
                throw new InvalidOperationException("A walk-in has no appointment to reschedule. Log the contact or cancel the waiting entry instead.");

            var now = clinicClock.UtcNow;
            var replacement = await appointmentService.RescheduleByClinicCoreAsync(appointmentId, newDoctorId, newDate, newStartTime, ct);
            var attempt = item.Resolve(DisruptionOutcome.Rescheduled, actorUserId, notes, now);
            await disruptions.AddContactAttemptAsync(attempt, ct);

            var message = new PatientNotification(
                item.PatientId,
                PatientNotificationKind.AppointmentRescheduledByClinic,
                "Appointment moved",
                $"Your appointment has been moved to {newDate:yyyy-MM-dd} at {newStartTime:HH\\:mm}.",
                replacement);
            return (replacement, message);
        }, cancellationToken);

        await publisher.PublishAsync(notification, cancellationToken);
        return replacementId;
    }

    public async Task CancelFromWorklistAsync(Guid itemId, Guid actorUserId, string? notes, CancellationToken cancellationToken)
    {
        var notification = await InTransactionAsync(async ct =>
        {
            var item = await LoadOpenItemAsync(itemId, ct);
            var now = clinicClock.UtcNow;
            var message = await CancelByClinicNowAsync(item, now, ct);

            var attempt = item.Resolve(DisruptionOutcome.Cancelled, actorUserId, notes, now);
            await disruptions.AddContactAttemptAsync(attempt, ct);
            return message;
        }, cancellationToken);

        await publisher.PublishAsync(notification, cancellationToken);
    }

    public async Task RecordDayEndAsync(Guid disruptionId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var notifications = await InTransactionAsync(async ct =>
        {
            var disruption = await disruptions.GetByIdAsync(disruptionId, ct)
                ?? throw new InvalidOperationException("The disruption was not found.");
            if (disruption.Date > clinicClock.Today)
                throw new InvalidOperationException("The day end cannot be recorded before the disruption date.");

            var now = clinicClock.UtcNow;
            disruption.RecordDayEnd(actorUserId, now);

            var pending = new List<PatientNotification>();
            foreach (var item in await disruptions.GetItemsForDisruptionAsync(disruptionId, ct))
            {
                if (!item.IsOpen)
                    continue;
                var cancelled = await TryCancelAutomaticallyAsync(item, now, onlyWhenDue: false, ct);
                if (cancelled is not null)
                    pending.Add(cancelled);
            }
            return pending;
        }, cancellationToken);

        await PublishAllAsync(notifications, cancellationToken);
    }

    public async Task<int> ProcessUnresolvedAsync(DateTimeOffset clinicNowUtc, CancellationToken cancellationToken)
    {
        var now = clinicNowUtc.ToUniversalTime();
        var notifications = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var pending = new List<PatientNotification>();
            foreach (var item in await disruptions.GetOpenItemsUpToDateAsync(clinicClock.Today, ct))
            {
                var cancelled = await TryCancelAutomaticallyAsync(item, now, onlyWhenDue: true, ct);
                if (cancelled is not null)
                    pending.Add(cancelled);
            }
            return pending;
        }, cancellationToken);

        await PublishAllAsync(notifications, cancellationToken);
        return notifications.Count;
    }

    /// <summary>
    /// Automatic clinic-unavailable cancellation of one open item. Returns the patient notification when the item was
    /// cancelled here, or null when it was left alone. Runs inside the caller's transaction.
    /// </summary>
    /// <remarks>
    /// Appointment items: only Confirmed appointments, only once their start has passed (when onlyWhenDue), and never
    /// while the patient is Called or in consultation. Walk-in items: only at day end, and only while Waiting.
    /// </remarks>
    private async Task<PatientNotification?> TryCancelAutomaticallyAsync(
        DisruptionWorklistItem item,
        DateTimeOffset now,
        bool onlyWhenDue,
        CancellationToken cancellationToken)
    {
        if (await CloseIfNoLongerNeededAsync(item, now, cancellationToken))
            return null;

        if (item.AppointmentId is Guid appointmentId)
        {
            var appointment = item.Appointment;
            if (appointment is null || appointment.Status != AppointmentStatus.Confirmed)
                return null;
            if (onlyWhenDue && clinicClock.ToUtc(appointment.AppointmentDate, appointment.StartTime) > now)
                return null;
            // The time-based path never cancels a patient who has checked in: staff handle them from the worklist.
            // Day end (onlyWhenDue false) still cancels whatever is unresolved.
            if (onlyWhenDue && await HasLiveAttemptAsync(appointmentId, cancellationToken))
                return null;
            if (await IsBeingServedAsync(appointmentId, cancellationToken))
                return null;

            var notification = await CancelAppointmentAsync(appointment, item.PatientId, cancellationToken);
            item.ResolveAutomatically(DisruptionOutcome.Cancelled, now);
            return notification;
        }

        if (onlyWhenDue)
            return null;

        var entry = item.QueueEntry;
        if (entry is null || entry.Status != QueueEntryStatus.Waiting)
            return null;

        var walkInNotification = await CancelWaitingWalkInAsync(entry, item.PatientId, now, cancellationToken);
        item.ResolveAutomatically(DisruptionOutcome.Cancelled, now);
        return walkInNotification;
    }

    /// <summary>Manual cancellation from the worklist. Refuses ineligible items with a clear message; the item is closed by the caller.</summary>
    private async Task<PatientNotification> CancelByClinicNowAsync(DisruptionWorklistItem item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (item.AppointmentId is Guid appointmentId)
        {
            var appointment = item.Appointment
                ?? throw new InvalidOperationException("The appointment was not found.");
            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new InvalidOperationException("Only a confirmed appointment can be cancelled from the worklist.");
            if (appointment.AppointmentDate < clinicClock.Today)
                throw new InvalidOperationException("Clinic cancellation and rescheduling apply only to today's and future appointments.");
            if (await IsBeingServedAsync(appointmentId, cancellationToken))
                throw new InvalidOperationException("The patient is already being served and cannot be cancelled from the worklist.");

            return await CancelAppointmentAsync(appointment, item.PatientId, cancellationToken);
        }

        var entry = item.QueueEntry ?? throw new InvalidOperationException("The waiting entry was not found.");
        if (entry.Status != QueueEntryStatus.Waiting)
            throw new InvalidOperationException("The patient is already being served and cannot be cancelled from the worklist.");

        return await CancelWaitingWalkInAsync(entry, item.PatientId, now, cancellationToken);
    }

    /// <summary>Clinic cancellation with reason clinic unavailable. Releases a Waiting check-in in the same transaction.</summary>
    private async Task<PatientNotification> CancelAppointmentAsync(Appointment appointment, Guid patientId, CancellationToken cancellationToken)
    {
        await appointmentService.CancelByClinicCoreAsync(appointment.Id, AppointmentCancellationReasons.ClinicUnavailable, cancellationToken);
        return new PatientNotification(
            patientId,
            PatientNotificationKind.AppointmentCancelledClinicUnavailable,
            "Appointment cancelled",
            $"Your appointment on {appointment.AppointmentDate:yyyy-MM-dd} at {appointment.StartTime:HH\\:mm} could not take place because the clinic was unavailable. Please contact the clinic to rebook.",
            appointment.Id);
    }

    private async Task<PatientNotification> CancelWaitingWalkInAsync(QueueEntry entry, Guid patientId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        entry.Cancel();
        await WithdrawPendingApprovalsAsync(entry.Id, now, cancellationToken);
        return new PatientNotification(
            patientId,
            PatientNotificationKind.WalkInCancelledClinicUnavailable,
            "Visit cancelled",
            "Your clinic visit could not take place because the clinic was unavailable. Please contact the clinic.",
            RelatedAppointmentId: null);
    }

    private async Task<bool> IsBeingServedAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var live = await queue.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        return live is not null && live.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation;
    }

    /// <summary>True when the patient holds a live attendance attempt (Waiting, Called or InConsultation) for the appointment.</summary>
    private async Task<bool> HasLiveAttemptAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        await queue.HasLiveQueueEntryForAppointmentAsync(appointmentId, cancellationToken);

    /// <summary>
    /// Closes an open item whose appointment or queue entry no longer needs staff action. Outcomes that describe reality
    /// (Cancelled, Rescheduled) are recorded as such; completed, No-Show or already-served items are closed with a note
    /// and keep their outcome. Returns true when the item was closed here.
    /// </summary>
    private async Task<bool> CloseIfNoLongerNeededAsync(DisruptionWorklistItem item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (item.AppointmentId is Guid appointmentId)
        {
            var appointment = item.Appointment;
            if (appointment is null)
                return false;

            switch (appointment.Status)
            {
                case AppointmentStatus.Cancelled:
                    item.ResolveAutomatically(DisruptionOutcome.Cancelled, now);
                    return true;
                case AppointmentStatus.Rescheduled:
                    item.ResolveAutomatically(DisruptionOutcome.Rescheduled, now);
                    return true;
                case AppointmentStatus.Completed:
                    item.CloseAsNoLongerNeeded("Appointment completed.", now);
                    return true;
                case AppointmentStatus.NoShow:
                    item.CloseAsNoLongerNeeded("Recorded as No-Show.", now);
                    return true;
                case AppointmentStatus.Rejected:
                    item.CloseAsNoLongerNeeded("Appointment was rejected.", now);
                    return true;
                case AppointmentStatus.Pending:
                    return false;
            }

            // Confirmed: the appointment still holds the slot. Only a served or finished attendance closes the item here.
            var attempt = await queue.GetByAppointmentIdAsync(appointmentId, cancellationToken);
            if (attempt is null)
                return false;
            if (attempt.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
            {
                item.CloseAsNoLongerNeeded("Patient is being served.", now);
                return true;
            }
            if (attempt.Status == QueueEntryStatus.Completed)
            {
                item.CloseAsNoLongerNeeded("Consultation completed.", now);
                return true;
            }
            return false;
        }

        var entry = item.QueueEntry;
        if (entry is null)
            return false;

        switch (entry.Status)
        {
            case QueueEntryStatus.Cancelled:
                item.ResolveAutomatically(DisruptionOutcome.Cancelled, now);
                return true;
            case QueueEntryStatus.Called:
            case QueueEntryStatus.InConsultation:
                item.CloseAsNoLongerNeeded("Patient is being served.", now);
                return true;
            case QueueEntryStatus.Completed:
                item.CloseAsNoLongerNeeded("Visit completed.", now);
                return true;
            case QueueEntryStatus.NoShow:
                item.CloseAsNoLongerNeeded("Recorded as No-Show.", now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Loads the item for a staff action. Closed items are refused so repeated submissions cannot double-act.</summary>
    private async Task<DisruptionWorklistItem> LoadOpenItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await disruptions.GetItemByIdAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("The worklist item was not found.");
        if (!item.IsOpen)
            throw new InvalidOperationException("This worklist item is already resolved.");
        return item;
    }

    private async Task WithdrawPendingApprovalsAsync(Guid queueEntryId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var approval in await approvals.GetForQueueEntriesAsync(new[] { queueEntryId }, cancellationToken))
        {
            if (approval.Decision == WalkInAdmissionDecision.Pending)
                approval.WithdrawPending(now);
        }
    }

    /// <summary>Blocks the remaining effective windows of the date from FromTime onward, with approved extensions included.</summary>
    private async Task<IReadOnlyList<DoctorBreak>> BuildBlockedTimeAsync(Guid doctorId, DateOnly date, TimeOnly fromTime, Guid disruptionId, CancellationToken cancellationToken)
    {
        var windows = await availability.GetEffectiveWindowsAsync(doctorId, date, cancellationToken);
        var blocks = new List<DoctorBreak>();
        foreach (var window in windows)
        {
            if (window.EndTime <= fromTime)
                continue;

            var start = window.StartTime > fromTime ? window.StartTime : fromTime;
            blocks.Add(DoctorBreak.CreateForDisruption(doctorId, date, start, window.EndTime, disruptionId));
        }

        return blocks;
    }

    /// <summary>
    /// Confirmed appointments that start at or after FromTime or are still running at FromTime, excluding patients
    /// already called or in consultation, plus every Waiting walk-in. A Waiting scheduled entry and its appointment
    /// form one item.
    /// </summary>
    private async Task<IReadOnlyList<DisruptionWorklistItem>> BuildWorklistAsync(
        Guid doctorId,
        DateOnly date,
        TimeOnly fromTime,
        Guid disruptionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var confirmed = await appointments.GetConfirmedForDoctorAndDateAsync(doctorId, date, cancellationToken);
        var active = await queue.GetActiveEntriesForDoctorAndDateAsync(doctorId, date, cancellationToken);

        var servedAppointmentIds = active
            .Where(entry => entry.AppointmentId is not null && entry.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
            .Select(entry => entry.AppointmentId!.Value)
            .ToHashSet();
        var waitingScheduledByAppointment = active
            .Where(entry => entry.Source == QueueEntrySource.Scheduled && entry.Status == QueueEntryStatus.Waiting && entry.AppointmentId is not null)
            .ToDictionary(entry => entry.AppointmentId!.Value);

        var items = new List<DisruptionWorklistItem>();
        foreach (var appointment in confirmed)
        {
            if (servedAppointmentIds.Contains(appointment.Id))
                continue;

            var waiting = waitingScheduledByAppointment.GetValueOrDefault(appointment.Id);
            var startsOrRunsAfterFrom = appointment.StartTime >= fromTime ||
                                        (appointment.StartTime < fromTime && appointment.EndTime > fromTime);
            if (!startsOrRunsAfterFrom && waiting is null)
                continue;

            items.Add(DisruptionWorklistItem.CreateForAppointment(disruptionId, appointment.PatientId, appointment.Id, waiting?.Id, now));
        }

        foreach (var walkIn in active.Where(entry => entry.Source == QueueEntrySource.WalkIn && entry.Status == QueueEntryStatus.Waiting))
            items.Add(DisruptionWorklistItem.CreateForQueueEntry(disruptionId, walkIn.PatientId, walkIn.Id, now));

        return items;
    }

    private async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var result = await action(cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task PublishAllAsync(IEnumerable<PatientNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
            await publisher.PublishAsync(notification, cancellationToken);
    }
}
