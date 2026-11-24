using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Application.Services;

public sealed class WalkInAdmissionApprovalService(
    IQueueRepository queueRepository,
    IAppointmentRepository appointments,
    IWalkInAdmissionApprovalRepository approvals,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IOptions<WalkInAdmissionApprovalOptions> options,
    IDoctorAvailabilityService availability,
    IDoctorCallWindowService callWindows) : IWalkInAdmissionApprovalService
{
    public async Task<Guid> RequestAsync(Guid queueEntryId, CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The walk-in queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.QueueDate, async ct =>
        {
            var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                ?? throw new InvalidOperationException("The walk-in queue entry was not found.");
            WalkInCallRules.EnsureWaitingWalkIn(entry);

            var today = clinicClock.Today;
            if (entry.QueueDate != today)
                throw new InvalidOperationException("Admission requests can only be submitted for today's walk-in queue.");

            if (await approvals.HasPendingForQueueEntryAsync(entry.Id, ct))
                throw new InvalidOperationException("A pending admission request already exists for this queue entry.");

            var now = clinicClock.UtcNow;
            var durationExpiry = now.AddMinutes(options.Value.PendingDurationMinutes);
            var clinicDayExpiry = clinicClock.ToUtc(today.AddDays(1), TimeOnly.MinValue);
            var approval = WalkInAdmissionApproval.Request(entry, now,
                durationExpiry < clinicDayExpiry ? durationExpiry : clinicDayExpiry);
            await approvals.AddAsync(approval, ct);
            return approval.Id;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingWalkInAdmissionRequest>> GetPendingForDoctorAsync(Guid doctorApplicationUserId, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorApplicationUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The authenticated account is not linked to an active doctor profile.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(doctor.Id, clinicClock.Today, async ct =>
        {
            var now = clinicClock.UtcNow;
            var today = clinicClock.Today;
            var pending = await approvals.GetPendingForDoctorAsync(doctor.Id, ct);
            foreach (var approval in pending)
            {
                if (approval.IsExpired(now, today, approval.QueueEntry.QueueDate))
                    approval.Expire(now);
            }

            return (IReadOnlyList<PendingWalkInAdmissionRequest>)pending
                .Where(approval => approval.Decision == WalkInAdmissionDecision.Pending)
                .Select(approval => new PendingWalkInAdmissionRequest(
                    approval.Id,
                    approval.QueueEntryId,
                    approval.QueueEntry.Patient.FullName,
                    approval.QueueEntry.QueueSequence!.Value,
                    approval.QueueEntry.ArrivedAt,
                    approval.ExpiresAtUtc))
                .ToArray();
        }, cancellationToken);
    }

    public async Task<string> DecideAsync(Guid approvalId, Guid doctorApplicationUserId, bool approve, string? notes, CancellationToken cancellationToken)
    {
        var preview = await approvals.GetByIdAsync(approvalId, cancellationToken)
            ?? throw new InvalidOperationException("The admission request was not found.");

        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorApplicationUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The authenticated account is not linked to an active doctor profile.");
        if (preview.DoctorId != doctor.Id)
            throw new UnauthorizedAccessException("Doctors may decide only admission requests for their own queue entries.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(doctor.Id, preview.QueueEntry.QueueDate, async ct =>
        {
            var approval = await approvals.GetByIdAsync(approvalId, ct)
                ?? throw new InvalidOperationException("The admission request was not found.");
            if (approval.DoctorId != doctor.Id)
                throw new UnauthorizedAccessException("Doctors may decide only admission requests for their own queue entries.");

            var now = clinicClock.UtcNow;
            var today = clinicClock.Today;
            if (approval.Decision == WalkInAdmissionDecision.Pending && approval.IsExpired(now, today, approval.QueueEntry.QueueDate))
            {
                approval.Expire(now);
                return "This admission request has expired.";
            }

            if (approval.Decision != WalkInAdmissionDecision.Pending)
                throw new InvalidOperationException("This admission request has already been decided or expired.");

            if (approve)
                approval.Approve(now, notes);
            else
                approval.Reject(now, notes);

            return approve ? "Walk-in admission approved." : "Walk-in admission rejected; the patient remains in FIFO order.";
        }, cancellationToken);
    }

    public async Task<WalkInCallResult> CallAsync(Guid queueEntryId, Guid approvalId, CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The walk-in queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.QueueDate, async ct =>
        {
            var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                ?? throw new InvalidOperationException("The walk-in queue entry was not found.");
            var approval = await approvals.GetByIdAsync(approvalId, ct)
                ?? throw new InvalidOperationException("The admission request was not found.");

            WalkInCallRules.EnsureWaitingWalkIn(entry);
            if (approval.QueueEntryId != entry.Id || approval.DoctorId != entry.DoctorId)
                throw new InvalidOperationException("The approval belongs to a different queue entry or doctor.");

            var now = clinicClock.UtcNow;
            var today = clinicClock.Today;
            if (approval.Decision == WalkInAdmissionDecision.Pending && approval.IsExpired(now, today, entry.QueueDate))
            {
                approval.Expire(now);
                return new WalkInCallResult(false, "The pending admission request expired; submit a new request if admission is still needed.");
            }

            var context = await LoadCallContextAsync(entry.DoctorId, today, now, ct);
            WalkInCallRules.ValidateAndCall(entry, approval, context);
            return new WalkInCallResult(true, "Walk-in called.");
        }, cancellationToken);
    }

    /// <summary>
    /// Loads the doctor-queue state the walk-in call rules need. Must run inside the doctor-queue transaction.
    /// Call Next builds the same context through WalkInCallContext so both paths evaluate identical inputs.
    /// </summary>
    private async Task<WalkInCallContext> LoadCallContextAsync(Guid doctorId, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorAsync(doctorId, cancellationToken);
        var activeEntries = await queueRepository.GetActiveEntriesForDoctorAndDateAsync(doctorId, today, cancellationToken);
        var workingHours = await availability.GetEffectiveWindowsAsync(doctorId, today, cancellationToken);
        var walkInIds = activeEntries.Where(entry => entry.Source == QueueEntrySource.WalkIn).Select(entry => entry.Id).ToArray();
        var walkInApprovals = await approvals.GetForQueueEntriesAsync(walkInIds, cancellationToken);
        var blockReason = await callWindows.GetCallBlockReasonAsync(doctorId, today, clinicClock.CurrentTime, cancellationToken);
        return new WalkInCallContext(today, now, clinicClock.CurrentTime, doctor is not null, activeEntries, workingHours,
            walkInApprovals.ToArray(), blockReason);
    }
}
