using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Read model for the Reception and Doctor queue pages. Nothing here changes state. Every instant is UTC; callers
/// format it in the clinic time zone.
/// </summary>
public sealed class QueueBoardService(
    IQueueBoardRepository boardRepository,
    IAppointmentRepository appointments,
    IWalkInAdmissionApprovalRepository approvals,
    IExpectedDurationService expectedDurationService,
    IClinicClock clinicClock,
    IDoctorDisruptionRepository disruptions) : IQueueBoardService
{
    public async Task<IReadOnlyList<DoctorChoice>> GetActiveDoctorsAsync(CancellationToken cancellationToken)
    {
        var doctors = await boardRepository.GetActiveDoctorsAsync(cancellationToken);
        return doctors.Select(doctor => new DoctorChoice(doctor.Id, doctor.FullName)).ToArray();
    }

    public async Task<DoctorQueueBoard> GetOwnDoctorQueueBoardAsync(Guid doctorApplicationUserId, DateOnly date, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorByApplicationUserIdAsync(doctorApplicationUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The authenticated account is not linked to an active doctor profile.");

        return await GetDoctorQueueBoardAsync(doctor.Id, date, cancellationToken);
    }

    public async Task<DoctorQueueBoard> GetDoctorQueueBoardAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        var doctor = await appointments.GetDoctorAsync(doctorId, cancellationToken)
            ?? throw new InvalidOperationException("The active doctor was not found.");

        var now = clinicClock.UtcNow;
        var today = clinicClock.Today;
        var expected = await expectedDurationService.CalculateExpectedDurationMinutesAsync(doctorId, date, cancellationToken);
        var expectedSpan = TimeSpan.FromMinutes(expected.DurationMinutes);

        var appointmentRows = await boardRepository.GetDoctorAppointmentsForDateAsync(doctorId, date, cancellationToken);
        var entries = await boardRepository.GetDoctorQueueEntriesForDateAsync(doctorId, date, cancellationToken);

        var activeEntry = entries
            .Where(entry => entry.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
            .OrderBy(entry => entry.CalledAt)
            .FirstOrDefault();

        var walkInEntries = entries
            .Where(entry => entry.Source == QueueEntrySource.WalkIn && entry.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Called)
            .OrderBy(entry => entry.ArrivedAt)
            .ThenBy(entry => entry.QueueSequence)
            .ToArray();

        var approvalRecords = await approvals.GetForQueueEntriesAsync(walkInEntries.Select(entry => entry.Id).ToArray(), cancellationToken);
        var approvalsByEntry = approvalRecords.ToLookup(approval => approval.QueueEntryId);

        var scheduled = appointmentRows
            .Where(item => item.Status is AppointmentStatus.Confirmed or AppointmentStatus.Completed or AppointmentStatus.NoShow)
            .OrderBy(item => item.StartTime)
            .Select(item => BuildScheduledItem(item, entries, date, expectedSpan, now))
            .ToList();

        var walkIns = walkInEntries
            .Select(entry => BuildWalkInItem(entry, approvalsByEntry[entry.Id].ToArray(), today, now))
            .ToList();

        AssignEstimates(scheduled, walkIns, activeEntry, expectedSpan, now);
        AttachDisruptionWarnings(doctor.FullName, date, await disruptions.GetOpenItemsForDoctorAndDateAsync(doctorId, date, cancellationToken), scheduled, walkIns);

        return new DoctorQueueBoard(
            doctor.Id,
            doctor.FullName,
            date,
            now,
            expected.DurationMinutes,
            expected.UsedHistoricalAverage,
            activeEntry is null ? null : BuildActiveEntry(activeEntry),
            scheduled,
            walkIns);
    }

    /// <summary>
    /// Warns on rows still waiting for the doctor (not served, completed or cancelled). The warning is informational only.
    /// </summary>
    private static void AttachDisruptionWarnings(
        string doctorName,
        DateOnly date,
        IReadOnlyList<DisruptionWorklistItem> openItems,
        List<BoardScheduledItem> scheduled,
        List<BoardWalkInItem> walkIns)
    {
        if (openItems.Count == 0)
            return;

        var byAppointment = openItems.Where(item => item.AppointmentId is not null)
            .DistinctBy(item => item.AppointmentId!.Value)
            .ToDictionary(item => item.AppointmentId!.Value);
        var byQueueEntry = openItems.Where(item => item.QueueEntryId is not null)
            .DistinctBy(item => item.QueueEntryId!.Value)
            .ToDictionary(item => item.QueueEntryId!.Value);

        for (var i = 0; i < scheduled.Count; i++)
        {
            var row = scheduled[i];
            if (!IsStillWaitingState(row.State) || !byAppointment.TryGetValue(row.AppointmentId, out var item))
                continue;
            scheduled[i] = row with { DisruptionWarning = DisruptionWarnings.ForAppointment(doctorName, item.Disruption.FromTime, item.Disruption.Reason) };
        }

        for (var i = 0; i < walkIns.Count; i++)
        {
            var row = walkIns[i];
            if (row.Status != QueueEntryStatus.Waiting || !byQueueEntry.TryGetValue(row.QueueEntryId, out var item))
                continue;
            walkIns[i] = row with { DisruptionWarning = DisruptionWarnings.ForQueueEntry(doctorName, item.Disruption.FromTime, item.Disruption.Reason) };
        }
    }

    private static bool IsStillWaitingState(ScheduledBoardState state) =>
        state is ScheduledBoardState.NotArrived or ScheduledBoardState.GraceRunning or ScheduledBoardState.GraceExpired or ScheduledBoardState.Waiting;

    private BoardScheduledItem BuildScheduledItem(
        Appointment appointment,
        IReadOnlyList<QueueEntry> entries,
        DateOnly date,
        TimeSpan expectedSpan,
        DateTimeOffset now)
    {
        var attempts = entries.Where(entry => entry.AppointmentId == appointment.Id).ToArray();
        // Live attempt first; otherwise the latest attempt (Cancelled means the patient left and may check in again).
        var latest = attempts.FirstOrDefault(entry => entry.Status.IsLive())
            ?? attempts.OrderByDescending(entry => entry.ArrivedAt).FirstOrDefault();
        var patientName = appointment.Patient.FullName;
        var patientPhone = appointment.Patient.PhoneNumber;

        if (latest is not null && latest.Status != QueueEntryStatus.Cancelled)
        {
            var state = latest.Status switch
            {
                QueueEntryStatus.Waiting => ScheduledBoardState.Waiting,
                QueueEntryStatus.Called => ScheduledBoardState.Called,
                QueueEntryStatus.InConsultation => ScheduledBoardState.InConsultation,
                QueueEntryStatus.Completed => ScheduledBoardState.Completed,
                _ => ScheduledBoardState.NoShow
            };
            return new BoardScheduledItem(appointment.Id, latest.Id, appointment.StartTime, patientName, patientPhone,
                state, GraceEndsAtUtc: null, EstimatedStartUtc: null, PreviouslyLeftQueue: false);
        }

        if (appointment.Status == AppointmentStatus.Completed)
            return new BoardScheduledItem(appointment.Id, null, appointment.StartTime, patientName, patientPhone,
                ScheduledBoardState.Completed, GraceEndsAtUtc: null, EstimatedStartUtc: null, PreviouslyLeftQueue: false);
        if (appointment.Status == AppointmentStatus.NoShow)
            return new BoardScheduledItem(appointment.Id, null, appointment.StartTime, patientName, patientPhone,
                ScheduledBoardState.NoShow, GraceEndsAtUtc: null, EstimatedStartUtc: null, PreviouslyLeftQueue: false);

        var previouslyLeft = latest is not null;
        var scheduledStart = clinicClock.ToUtc(date, appointment.StartTime);

        // A frozen grace period shows its real end. Before the grace clock starts, show the end it would have
        // with the current expected duration (an estimate, labelled as such in the UI).
        DateTimeOffset? graceEnd = null;
        if (appointment.FrozenGracePeriodMinutes is int frozen && appointment.GracePeriodStartedAtUtc is DateTimeOffset started)
            graceEnd = started.AddMinutes(frozen);
        else if (now >= scheduledStart)
            graceEnd = scheduledStart + expectedSpan * ExpectedDurationOptions.GracePeriodMultiplier;

        if (graceEnd is null)
            return new BoardScheduledItem(appointment.Id, null, appointment.StartTime, patientName, patientPhone,
                ScheduledBoardState.NotArrived, GraceEndsAtUtc: null, EstimatedStartUtc: null, previouslyLeft);

        var graceState = now >= graceEnd.Value ? ScheduledBoardState.GraceExpired : ScheduledBoardState.GraceRunning;
        return new BoardScheduledItem(appointment.Id, null, appointment.StartTime, patientName, patientPhone,
            graceState, graceEnd, EstimatedStartUtc: null, previouslyLeft);
    }

    private static BoardWalkInItem BuildWalkInItem(QueueEntry entry, WalkInAdmissionApproval[] entryApprovals, DateOnly today, DateTimeOffset now)
    {
        var (state, usableApprovalId) = ResolveApprovalState(entryApprovals, entry.QueueDate, today, now);
        return new BoardWalkInItem(
            entry.Id,
            entry.QueueSequence ?? 0,
            entry.Patient.FullName,
            entry.Patient.PhoneNumber,
            entry.ArrivedAt,
            entry.Status,
            state,
            usableApprovalId,
            EstimatedStartUtc: null);
    }

    /// <summary>
    /// A usable record (Approved, unused, unexpired) wins. Otherwise the latest record decides. Expiry is evaluated
    /// against the current time and clinic day, not the stored state.
    /// </summary>
    private static (WalkInApprovalState State, Guid? UsableApprovalId) ResolveApprovalState(
        WalkInAdmissionApproval[] records, DateOnly queueDate, DateOnly today, DateTimeOffset now)
    {
        if (records.Length == 0)
            return (WalkInApprovalState.None, null);

        var usable = records
            .Where(record => record.Decision == WalkInAdmissionDecision.Approved && record.UsedAtUtc is null &&
                             !record.IsExpired(now, today, queueDate))
            .OrderByDescending(record => record.DecisionAtUtc)
            .FirstOrDefault();
        if (usable is not null)
            return (WalkInApprovalState.Approved, usable.Id);

        var latest = records.OrderByDescending(record => record.RequestedAtUtc).First();
        var state = latest.Decision switch
        {
            WalkInAdmissionDecision.Pending => latest.IsExpired(now, today, queueDate)
                ? WalkInApprovalState.Expired
                : WalkInApprovalState.Pending,
            WalkInAdmissionDecision.Approved => latest.UsedAtUtc is not null
                ? WalkInApprovalState.Used
                : WalkInApprovalState.Expired,
            WalkInAdmissionDecision.Rejected => WalkInApprovalState.Rejected,
            _ => WalkInApprovalState.Expired
        };
        return (state, null);
    }

    private static BoardActiveEntry BuildActiveEntry(QueueEntry entry) =>
        new(entry.Id, entry.Source, entry.Status, entry.Patient.FullName, entry.Patient.PhoneNumber,
            entry.QueueSequence, entry.Appointment?.StartTime, entry.CalledAt, entry.ConsultationStartedAt);

    /// <summary>
    /// Estimates follow the Call Next order: waiting scheduled patients by start time, then waiting walk-ins FIFO.
    /// estimate_i = base + i * expected, where base is now when nobody is active, max(now, consultationStart + expected)
    /// while a consultation is in progress, and now + expected when a patient is Called but not yet in consultation.
    /// </summary>
    private static void AssignEstimates(
        List<BoardScheduledItem> scheduled,
        List<BoardWalkInItem> walkIns,
        QueueEntry? activeEntry,
        TimeSpan expectedSpan,
        DateTimeOffset now)
    {
        DateTimeOffset first;
        if (activeEntry is null)
            first = now;
        else if (activeEntry.Status == QueueEntryStatus.InConsultation && activeEntry.ConsultationStartedAt is DateTimeOffset started)
            first = Max(now, started + expectedSpan);
        else
            first = now + expectedSpan;

        var position = 0;
        for (var i = 0; i < scheduled.Count; i++)
        {
            if (scheduled[i].State != ScheduledBoardState.Waiting)
                continue;
            scheduled[i] = scheduled[i] with { EstimatedStartUtc = first + expectedSpan * position };
            position++;
        }

        // Call Next takes approved walk-ins in FIFO order first; walk-ins without a usable approval are called only once
        // approved, so they are estimated after the approved ones (each group keeps FIFO order).
        var callOrder = walkIns
            .Select((row, index) => (row, index))
            .Where(pair => pair.row.Status == QueueEntryStatus.Waiting)
            .OrderBy(pair => pair.row.UsableApprovalId is null ? 1 : 0)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.index)
            .ToArray();
        foreach (var index in callOrder)
        {
            walkIns[index] = walkIns[index] with { EstimatedStartUtc = first + expectedSpan * position };
            position++;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;
}
