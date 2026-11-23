using MediCare.Application.Common;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
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
    IOptions<WalkInAdmissionApprovalOptions> options) : IWalkInAdmissionApprovalService
{
    public async Task<Guid> RequestAsync(Guid queueEntryId, CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The walk-in queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(
            preview.DoctorProfileId,
            preview.QueueDate,
            async ct =>
            {
                var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                    ?? throw new InvalidOperationException("The walk-in queue entry was not found.");

                ValidateWaitingWalkIn(entry);

                var today = clinicClock.Today;
                if (entry.QueueDate != today)
                {
                    throw new InvalidOperationException(
                        "Admission requests can only be submitted for today's walk-in queue.");
                }

                if (await approvals.HasPendingForQueueEntryAsync(entry.Id, ct))
                {
                    throw new InvalidOperationException(
                        "A pending admission request already exists for this queue entry.");
                }

                var now = clinicClock.UtcNow;
                var durationExpiry = now.AddMinutes(options.Value.PendingDurationMinutes);
                var clinicDayExpiry = clinicClock.ToUtc(today.AddDays(1), TimeOnly.MinValue);
                var expiry = durationExpiry < clinicDayExpiry ? durationExpiry : clinicDayExpiry;

                var approval = WalkInAdmissionApproval.Request(entry, now, expiry);
                await approvals.AddAsync(approval, ct);
                return approval.Id;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<PendingWalkInAdmissionRequest>> GetPendingForDoctorAsync(
        Guid doctorUserId,
        CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorProfileByUserIdAsync(doctorUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "The authenticated account is not linked to an active doctor profile.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(
            doctor.Id,
            clinicClock.Today,
            async ct =>
            {
                var now = clinicClock.UtcNow;
                var today = clinicClock.Today;
                var pending = await approvals.GetPendingForDoctorAsync(doctor.Id, ct);

                foreach (var approval in pending)
                {
                    if (approval.IsExpired(now, today, approval.QueueEntry.QueueDate))
                    {
                        approval.Expire(now);
                    }
                }

                var requests = new List<PendingWalkInAdmissionRequest>();
                foreach (var approval in pending.Where(item => item.Decision == WalkInAdmissionDecision.Pending))
                {
                    var patientName = await appointments.GetPatientNameAsync(
                        approval.QueueEntry.PatientProfileId, ct);

                    requests.Add(new PendingWalkInAdmissionRequest(
                        approval.Id,
                        approval.QueueEntryId,
                        patientName ?? "Unknown patient",
                        approval.QueueEntry.QueueSequence!.Value,
                        approval.QueueEntry.ArrivedAt,
                        approval.ExpiresAtUtc));
                }

                return (IReadOnlyList<PendingWalkInAdmissionRequest>)requests;
            },
            cancellationToken);
    }

    public async Task<string> DecideAsync(
        Guid approvalId,
        Guid doctorUserId,
        bool approve,
        string? notes,
        CancellationToken cancellationToken)
    {
        var preview = await approvals.GetByIdAsync(approvalId, cancellationToken)
            ?? throw new InvalidOperationException("The admission request was not found.");

        var doctor = await appointments.GetDoctorProfileByUserIdAsync(doctorUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "The authenticated account is not linked to an active doctor profile.");

        if (preview.DoctorProfileId != doctor.Id)
        {
            throw new UnauthorizedAccessException(
                "Doctors may decide only admission requests for their own queue entries.");
        }

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(
            doctor.Id,
            preview.QueueEntry.QueueDate,
            async ct =>
            {
                var approval = await approvals.GetByIdAsync(approvalId, ct)
                    ?? throw new InvalidOperationException("The admission request was not found.");

                if (approval.DoctorProfileId != doctor.Id)
                {
                    throw new UnauthorizedAccessException(
                        "Doctors may decide only admission requests for their own queue entries.");
                }

                var now = clinicClock.UtcNow;
                var today = clinicClock.Today;

                if (approval.Decision == WalkInAdmissionDecision.Pending &&
                    approval.IsExpired(now, today, approval.QueueEntry.QueueDate))
                {
                    approval.Expire(now);
                    return "This admission request has expired.";
                }

                if (approval.Decision != WalkInAdmissionDecision.Pending)
                {
                    throw new InvalidOperationException(
                        "This admission request has already been decided or expired.");
                }

                if (approve)
                {
                    approval.Approve(now, notes);
                }
                else
                {
                    approval.Reject(now, notes);
                }

                return approve
                    ? "Walk-in admission approved."
                    : "Walk-in admission rejected; the patient remains in FIFO order.";
            },
            cancellationToken);
    }

    public async Task<WalkInCallResult> CallAsync(
        Guid queueEntryId,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        var preview = await queueRepository.GetByIdAsync(queueEntryId, cancellationToken)
            ?? throw new InvalidOperationException("The walk-in queue entry was not found.");

        return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(
            preview.DoctorProfileId,
            preview.QueueDate,
            async ct =>
            {
                var entry = await queueRepository.GetByIdAsync(queueEntryId, ct)
                    ?? throw new InvalidOperationException("The walk-in queue entry was not found.");
                var approval = await approvals.GetByIdAsync(approvalId, ct)
                    ?? throw new InvalidOperationException("The admission request was not found.");

                ValidateWaitingWalkIn(entry);

                if (approval.QueueEntryId != entry.Id || approval.DoctorProfileId != entry.DoctorProfileId)
                {
                    throw new InvalidOperationException(
                        "The approval belongs to a different queue entry or doctor.");
                }

                var now = clinicClock.UtcNow;
                var today = clinicClock.Today;

                if (approval.Decision == WalkInAdmissionDecision.Pending &&
                    approval.IsExpired(now, today, entry.QueueDate))
                {
                    approval.Expire(now);
                    return new WalkInCallResult(
                        false,
                        "The pending admission request expired; submit a new request if admission is still needed.");
                }

                if (approval.Decision != WalkInAdmissionDecision.Approved)
                {
                    throw new InvalidOperationException(
                        approval.Decision == WalkInAdmissionDecision.Expired
                            ? "This admission approval has expired."
                            : "A current approved admission request is required before calling a walk-in.");
                }

                if (approval.IsExpired(now, today, entry.QueueDate))
                {
                    throw new InvalidOperationException("This admission approval has expired.");
                }

                if (entry.QueueDate != today)
                {
                    throw new InvalidOperationException("Only today's walk-in entries can be called.");
                }

                if (approval.UsedAtUtc is not null)
                {
                    throw new InvalidOperationException("This admission approval has already been used.");
                }

                if (await appointments.GetDoctorProfileAsync(entry.DoctorProfileId, ct) is null)
                {
                    throw new InvalidOperationException("The doctor is not active.");
                }

                var activeEntries = await queueRepository.GetActiveEntriesForDoctorAndDateAsync(
                    entry.DoctorProfileId, today, ct);

                if (activeEntries.Any(other =>
                        other.Id != entry.Id &&
                        other.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation))
                {
                    throw new InvalidOperationException(
                        "The doctor already has a patient Called or InConsultation.");
                }

                if (activeEntries.Any(other =>
                        other.Id != entry.Id &&
                        other.Source == QueueEntrySource.Scheduled &&
                        other.Status == QueueEntryStatus.Waiting))
                {
                    throw new InvalidOperationException(
                        "A scheduled patient has checked in and is waiting; call that patient before a walk-in.");
                }

                if (activeEntries.Any(other =>
                        other.Id != entry.Id &&
                        other.Source == QueueEntrySource.WalkIn &&
                        other.Status == QueueEntryStatus.Waiting &&
                        IsBeforeInFifo(other, entry)))
                {
                    throw new InvalidOperationException("An earlier walk-in is ahead in FIFO order.");
                }

                var workingHours = await appointments.GetWorkingHoursAsync(
                    entry.DoctorProfileId, today.DayOfWeek, ct);

                if (!workingHours.Any(hours =>
                        hours.StartTime <= clinicClock.CurrentTime &&
                        clinicClock.CurrentTime < hours.EndTime))
                {
                    throw new InvalidOperationException("The doctor is outside configured working hours.");
                }

                entry.CallWalkIn(approval, now, today);
                return new WalkInCallResult(true, "Walk-in called.");
            },
            cancellationToken);
    }

    private static void ValidateWaitingWalkIn(QueueEntry entry)
    {
        if (entry.Source != QueueEntrySource.WalkIn)
        {
            throw new InvalidOperationException(
                "Only walk-in queue entries can request or use walk-in admission approval.");
        }

        if (entry.Status != QueueEntryStatus.Waiting)
        {
            throw new InvalidOperationException("Only a Waiting walk-in entry can be admitted.");
        }
    }

    private static bool IsBeforeInFifo(QueueEntry candidate, QueueEntry entry) =>
        candidate.ArrivedAt < entry.ArrivedAt ||
        (candidate.ArrivedAt == entry.ArrivedAt && candidate.QueueSequence < entry.QueueSequence);
}
