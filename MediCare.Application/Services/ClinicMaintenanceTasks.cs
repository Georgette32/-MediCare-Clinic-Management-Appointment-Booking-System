using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Clinic-day housekeeping run by the maintenance job. Every method isolates its own units of work: one doctor or one
/// queue entry that fails is reported in Failures and does not stop the others.
/// </summary>
public interface IClinicMaintenanceTasks
{
    /// <summary>Starts eligible grace periods and converts expired ones to No-Show, for every active doctor, for the current clinic date.</summary>
    Task<GracePeriodSweepResult> ProcessGracePeriodsForActiveDoctorsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes attendance attempts left over from earlier clinic days: Waiting and Called become Cancelled; InConsultation
    /// becomes Completed (and a scheduled appointment is marked Completed when it is still Confirmed).
    /// </summary>
    Task<StaleQueueSweepResult> SweepStaleQueueEntriesAsync(CancellationToken cancellationToken);

    /// <summary>Makes a scheduled appointment-length change the current length once its effective date has come, for every doctor that has one.</summary>
    Task<DurationPromotionResult> PromoteDueDurationChangesAsync(CancellationToken cancellationToken);
}

public sealed record GracePeriodSweepResult(int DoctorsProcessed, int Changes, IReadOnlyList<string> Failures);

public sealed record StaleQueueSweepResult(int Cancelled, int Completed, IReadOnlyList<string> Failures);

public sealed record DurationPromotionResult(int Promoted, IReadOnlyList<string> Failures);

public sealed class ClinicMaintenanceTasks(
    IQueueBoardRepository boardRepository,
    IGracePeriodService gracePeriods,
    IQueueRepository queue,
    IWalkInAdmissionApprovalRepository approvals,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IDoctorScheduleManagementRepository durationChanges) : IClinicMaintenanceTasks
{
    public async Task<DurationPromotionResult> PromoteDueDurationChangesAsync(CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var doctors = await durationChanges.GetDoctorsWithSpecializationAsync(activeOnly: false, cancellationToken);
        var promoted = 0;
        var failures = new List<string>();

        foreach (var candidate in doctors.Where(doctor => doctor.NextDurationEffectiveFrom is { } effectiveFrom && effectiveFrom <= today))
        {
            try
            {
                var changed = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
                {
                    var doctor = await durationChanges.GetDoctorRecordAsync(candidate.Id, ct);
                    if (doctor is null || !doctor.PromoteDueDurationChange(today))
                        return false;
                    await unitOfWork.SaveChangesAsync(ct);
                    return true;
                }, cancellationToken);
                if (changed)
                    promoted++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"Doctor {candidate.Id:D}: {exception.Message}");
            }
        }

        return new DurationPromotionResult(promoted, failures);
    }

    public async Task<GracePeriodSweepResult> ProcessGracePeriodsForActiveDoctorsAsync(CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var doctors = await boardRepository.GetActiveDoctorsAsync(cancellationToken);
        var processed = 0;
        var changes = 0;
        var failures = new List<string>();

        foreach (var doctor in doctors)
        {
            try
            {
                changes += await gracePeriods.ProcessDoctorDayAsync(doctor.Id, today, cancellationToken);
                processed++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"Doctor {doctor.Id:D}: {exception.Message}");
            }
        }

        return new GracePeriodSweepResult(processed, changes, failures);
    }

    public async Task<StaleQueueSweepResult> SweepStaleQueueEntriesAsync(CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var stale = await queue.GetLiveEntriesBeforeDateAsync(today, cancellationToken);
        var cancelled = 0;
        var completed = 0;
        var failures = new List<string>();

        foreach (var candidate in stale)
        {
            try
            {
                var outcome = await unitOfWork.ExecuteDoctorQueueWithRetryAsync(candidate.DoctorId, candidate.QueueDate,
                    ct => CloseStaleEntryAsync(candidate.Id, today, ct), cancellationToken);
                if (outcome == StaleOutcome.Cancelled)
                    cancelled++;
                else if (outcome == StaleOutcome.Completed)
                    completed++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"Queue entry {candidate.Id:D}: {exception.Message}");
            }
        }

        return new StaleQueueSweepResult(cancelled, completed, failures);
    }

    /// <summary>Applies the stale rule to one entry, reloaded inside the doctor-queue transaction.</summary>
    private async Task<StaleOutcome> CloseStaleEntryAsync(Guid queueEntryId, DateOnly today, CancellationToken cancellationToken)
    {
        var entry = await queue.GetByIdAsync(queueEntryId, cancellationToken);
        if (entry is null || entry.QueueDate >= today)
            return StaleOutcome.Skipped;

        var now = clinicClock.UtcNow;
        switch (entry.Status)
        {
            case QueueEntryStatus.Waiting:
            case QueueEntryStatus.Called:
                entry.CancelAsStale();
                await WithdrawPendingApprovalsAsync(entry.Id, now, cancellationToken);
                return StaleOutcome.Cancelled;

            case QueueEntryStatus.InConsultation:
                entry.Complete(now);
                if (entry.Source == QueueEntrySource.Scheduled && entry.Appointment is { Status: AppointmentStatus.Confirmed } appointment)
                    appointment.MarkCompleted(now);
                return StaleOutcome.Completed;

            default:
                return StaleOutcome.Skipped;
        }
    }

    private async Task WithdrawPendingApprovalsAsync(Guid queueEntryId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var approval in await approvals.GetForQueueEntriesAsync(new[] { queueEntryId }, cancellationToken))
        {
            if (approval.Decision == WalkInAdmissionDecision.Pending)
                approval.WithdrawPending(now);
        }
    }

    private enum StaleOutcome
    {
        Skipped,
        Cancelled,
        Completed
    }
}
