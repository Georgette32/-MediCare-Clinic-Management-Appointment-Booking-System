using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class DoctorScheduleService(
    IAppointmentRepository appointments,
    IDoctorScheduleRepository schedule,
    IDoctorScheduleManagementRepository management,
    IDoctorAvailabilityService availability,
    IDoctorDisruptionRepository disruptions,
    IUnitOfWork unitOfWork,
    IClinicClock clock,
    IPatientNotificationPublisher? patientNotifications = null) : IDoctorScheduleService
{
    public const int BreakHorizonDays = 60;
    private const int MaxHolidayReasonLength = 500;
    private static readonly TimeSpan LatestOverflowEnd = new(23, 55, 0);
    private readonly IPatientNotificationPublisher _notifications = patientNotifications ?? SilentPatientNotificationPublisher.Instance;

    public async Task<Guid?> GetOwnDoctorIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        (await appointments.GetDoctorByApplicationUserIdAsync(applicationUserId, cancellationToken))?.Id;

    public async Task<DoctorScheduleView> GetScheduleAsync(ScheduleActor actor, Guid doctorId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, doctorId, cancellationToken);
        var doctor = await management.GetDoctorRecordAsync(doctorId, cancellationToken)
            ?? throw new InvalidOperationException("The doctor was not found.");

        var today = clock.Today;
        var now = clock.CurrentTime;
        var windows = (await management.GetAllWorkingHoursAsync(doctorId, cancellationToken))
            .OrderBy(item => MondayFirst(item.DayOfWeek))
            .ThenBy(item => item.StartTime)
            .Select(item => new WorkingWindowItem(item.Id, item.DayOfWeek, item.StartTime, item.EndTime, item.IsActive))
            .ToArray();

        var breaks = (await management.GetBreaksInRangeAsync(doctorId, today, today.AddDays(BreakHorizonDays), cancellationToken))
            .OrderBy(item => item.Date).ThenBy(item => item.StartTime)
            .Select(item => new DoctorBreakItem(item.Id, item.Date, item.StartTime, item.EndTime, item.DisruptionId is not null,
                item.DisruptionId is null && IsUpcoming(item.Date, item.EndTime, today, now)))
            .ToArray();

        var holidays = (await management.GetHolidaysEndingOnOrAfterAsync(doctorId, today, cancellationToken))
            .OrderBy(item => item.StartDate)
            .Select(item => new DoctorHolidayItem(item.Id, item.StartDate, item.EndDate, item.Reason, true))
            .ToArray();

        return new DoctorScheduleView(
            doctor.Id, doctor.FullName, doctor.Specialization?.Name ?? string.Empty, doctor.DurationOn(today), doctor.Biography,
            doctor.IsActive, today, windows, breaks, holidays,
            doctor.NextAppointmentDurationMinutes, doctor.NextDurationEffectiveFrom);
    }

    public Task<ManagementResult> AddWorkingWindowAsync(ScheduleActor actor, Guid doctorId, DayOfWeek day, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            await RequireDoctorAsync(doctorId, ct);
            var windows = await management.GetAllWorkingHoursAsync(doctorId, ct);
            // Validates the range before any other rule.
            var window = DoctorWorkingHour.Create(doctorId, day, startTime, endTime);
            EnsureNoActiveOverlap(windows, day, startTime, endTime, exceptWindowId: null);
            if (windows.Any(item => item.DayOfWeek == day && item.StartTime == startTime))
                throw new InvalidOperationException(
                    $"A working window already starts at {startTime:h\\:mm tt} on {day}. Reactivate or edit that window instead.");

            await management.AddWorkingHourAsync(window, ct);
            await RecordHistoryAsync(window, baseline: null, ct);
            return $"Working window added: {day} {startTime:h\\:mm tt}-{endTime:h\\:mm tt}.";
        }, cancellationToken);

    public Task<ManagementResult> ChangeWorkingWindowTimesAsync(ScheduleActor actor, Guid doctorId, Guid workingWindowId, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            var windows = await management.GetAllWorkingHoursAsync(doctorId, ct);
            var window = FindWindow(windows, workingWindowId);
            var oldStart = window.StartTime;
            var oldEnd = window.EndTime;
            var wasActive = window.IsActive;
            var baseline = await CaptureBaselineIfLegacyAsync(window, ct);

            if (windows.Any(item => item.Id != window.Id && item.DayOfWeek == window.DayOfWeek && item.StartTime == startTime))
                throw new InvalidOperationException($"Another working window already starts at {startTime:h\\:mm tt} on {window.DayOfWeek}.");
            if (wasActive)
                EnsureNoActiveOverlap(windows, window.DayOfWeek, startTime, endTime, exceptWindowId: window.Id);

            window.ChangeTimes(startTime, endTime);

            if (wasActive)
            {
                if (endTime != oldEnd)
                    await EnsureNoLiveExtensionsForEndAsync(doctorId, oldEnd, ct);
                if (!(startTime <= oldStart && endTime >= oldEnd))
                    await EnsureFutureAppointmentsFitAsync(doctor, windows, ct);
            }

            await RecordHistoryAsync(window, baseline, ct);
            return $"Working window updated: {window.DayOfWeek} {startTime:h\\:mm tt}-{endTime:h\\:mm tt}.";
        }, cancellationToken);

    public Task<ManagementResult> SetWorkingWindowActiveAsync(ScheduleActor actor, Guid doctorId, Guid workingWindowId, bool isActive, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            var windows = await management.GetAllWorkingHoursAsync(doctorId, ct);
            var window = FindWindow(windows, workingWindowId);
            if (window.IsActive == isActive)
                return $"The working window is already {(isActive ? "active" : "inactive")}.";

            var baseline = await CaptureBaselineIfLegacyAsync(window, ct);
            if (isActive)
            {
                EnsureNoActiveOverlap(windows, window.DayOfWeek, window.StartTime, window.EndTime, exceptWindowId: window.Id);
                window.Activate();
                await RecordHistoryAsync(window, baseline, ct);
                return "Working window reactivated.";
            }

            window.Deactivate();
            await EnsureNoLiveExtensionsForEndAsync(doctorId, window.EndTime, ct);
            await EnsureFutureAppointmentsFitAsync(doctor, windows, ct);
            await RecordHistoryAsync(window, baseline, ct);
            return "Working window deactivated.";
        }, cancellationToken);

    /// <summary>
    /// Schedules a new appointment length from <paramref name="effectiveFrom"/> on. Booked days from that date on are
    /// re-timed onto the new grid, and a day that no longer fits is extended. Earlier days are not touched.
    /// </summary>
    public Task<ManagementResult> ChangeAppointmentDurationAsync(ScheduleActor actor, Guid doctorId, int minutes, DateOnly effectiveFrom, CancellationToken cancellationToken) =>
        ChangeWithNotificationsAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            doctor.ScheduleDurationChange(minutes, effectiveFrom, clock.Today);
            var summary = await ReconcileAsync(doctor, actor, ct);
            var header = doctor.NextAppointmentDurationMinutes is null
                ? $"Appointment duration stays {doctor.DurationOn(clock.Today)} min."
                : $"Appointment duration will be {minutes} min from {effectiveFrom:yyyy-MM-dd}.";
            return new ScheduleChangeOutcome($"{header} {summary.Describe()}", summary.Notifications);
        }, cancellationToken);

    /// <summary>Drops a scheduled length change. Appointments on the days it had moved are put back onto the old grid.</summary>
    public Task<ManagementResult> CancelScheduledDurationChangeAsync(ScheduleActor actor, Guid doctorId, CancellationToken cancellationToken) =>
        ChangeWithNotificationsAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            if (doctor.NextAppointmentDurationMinutes is null)
                throw new InvalidOperationException("There is no scheduled duration change to cancel.");
            doctor.CancelScheduledDurationChange();
            var summary = await ReconcileAsync(doctor, actor, ct);
            return new ScheduleChangeOutcome(
                $"Scheduled duration change cancelled. Appointment duration stays {doctor.AppointmentDurationMinutes} min. {summary.Describe()}",
                summary.Notifications);
        }, cancellationToken);

    public Task<ManagementResult> AddBreakAsync(ScheduleActor actor, Guid doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            await RequireDoctorAsync(doctorId, ct);
            var item = DoctorBreak.Create(doctorId, date, startTime, endTime);
            var today = clock.Today;
            if (!IsUpcoming(date, endTime, today, clock.CurrentTime))
                throw new InvalidOperationException("Breaks can only be added for a time that has not yet ended.");

            var windows = await availability.GetEffectiveWindowsAsync(doctorId, date, ct);
            if (!windows.Any(window => startTime >= window.StartTime && endTime <= window.EndTime))
                throw new InvalidOperationException("The break must fall inside one of the doctor's working windows on that date.");

            var sameDay = await management.GetBreaksInRangeAsync(doctorId, date, date, ct);
            var overlappingBreak = sameDay.FirstOrDefault(existing => existing.StartTime < endTime && existing.EndTime > startTime);
            if (overlappingBreak is not null)
                throw new InvalidOperationException(
                    $"This break overlaps another break that day ({overlappingBreak.StartTime:h\\:mm tt}-{overlappingBreak.EndTime:h\\:mm tt}).");

            var booked = (await management.GetLiveAppointmentsAsync(doctorId, date, date, ct))
                .FirstOrDefault(appointment => appointment.Start < endTime && appointment.End > startTime);
            if (booked is not null)
                throw new InvalidOperationException(
                    $"A patient is booked from {booked.Start:h\\:mm tt} to {booked.End:h\\:mm tt}, which overlaps this break. Reschedule that appointment first.");

            await management.AddBreakAsync(item, ct);
            return $"Break added: {date:yyyy-MM-dd} {startTime:h\\:mm tt}-{endTime:h\\:mm tt}.";
        }, cancellationToken);

    public Task<ManagementResult> RemoveBreakAsync(ScheduleActor actor, Guid doctorId, Guid breakId, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            await RequireDoctorAsync(doctorId, ct);
            var item = await management.GetBreakAsync(breakId, ct);
            if (item is null || item.DoctorId != doctorId)
                throw new InvalidOperationException("The break was not found.");
            if (item.DisruptionId is not null)
                throw new InvalidOperationException("Breaks created by a doctor-leaves-early disruption cannot be removed here.");
            if (!IsUpcoming(item.Date, item.EndTime, clock.Today, clock.CurrentTime))
                throw new InvalidOperationException("Only future breaks can be removed.");

            management.RemoveBreak(item);
            return "Break removed.";
        }, cancellationToken);

    public Task<ManagementResult> AddHolidayAsync(ScheduleActor actor, Guid doctorId, DateOnly startDate, DateOnly endDate, string? reason, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            await RequireDoctorAsync(doctorId, ct);
            var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (trimmedReason is not null && trimmedReason.Length > MaxHolidayReasonLength)
                throw new InvalidOperationException($"The holiday reason cannot exceed {MaxHolidayReasonLength} characters.");
            var holiday = DoctorHoliday.Create(doctorId, startDate, endDate, trimmedReason);
            if (startDate < clock.Today)
                throw new InvalidOperationException("A holiday must start today or later.");

            var booked = await management.GetLiveAppointmentsAsync(doctorId, startDate, endDate, ct);
            if (booked.Count > 0)
                throw new InvalidOperationException(
                    $"{booked.Count} booked appointment(s) fall between {startDate:yyyy-MM-dd} and {endDate:yyyy-MM-dd}. Reschedule them first, then add the holiday.");

            await management.AddHolidayAsync(holiday, ct);
            return $"Holiday added: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}.";
        }, cancellationToken);

    public Task<ManagementResult> RemoveHolidayAsync(ScheduleActor actor, Guid doctorId, Guid holidayId, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            await RequireDoctorAsync(doctorId, ct);
            var holiday = await management.GetHolidayAsync(holidayId, ct);
            if (holiday is null || holiday.DoctorId != doctorId)
                throw new InvalidOperationException("The holiday was not found.");
            if (holiday.EndDate < clock.Today)
                throw new InvalidOperationException("Only current or future holidays can be removed.");

            management.RemoveHoliday(holiday);
            return "Holiday removed.";
        }, cancellationToken);

    public Task<ManagementResult> UpdateBiographyAsync(ScheduleActor actor, Guid doctorId, string? biography, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            doctor.UpdateBiography(biography);
            return "Profile biography saved.";
        }, cancellationToken);

    /// <summary>Ownership is checked before any write. A doctor acting on another doctor's profile is refused by exception.</summary>
    private async Task AuthorizeAsync(ScheduleActor actor, Guid doctorId, CancellationToken cancellationToken)
    {
        if (actor.IsAdmin)
            return;

        var own = await appointments.GetDoctorByApplicationUserIdAsync(actor.UserId, cancellationToken);
        if (own is null || own.Id != doctorId)
            throw new UnauthorizedAccessException("Doctors may manage only their own schedule.");
    }

    private async Task<ManagementResult> ChangeAsync(ScheduleActor actor, Guid doctorId, Func<CancellationToken, Task<string>> change, CancellationToken cancellationToken)
    {
        try
        {
            await AuthorizeAsync(actor, doctorId, cancellationToken);
            // Each change runs in one serializable transaction. Entities are loaded inside it, after the unit of work
            // clears its change tracker.
            var message = await unitOfWork.ExecuteSerializableWithRetryAsync(change, cancellationToken);
            return ManagementResult.Success(message);
        }
        catch (InvalidOperationException exception)
        {
            return ManagementResult.Failure(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ManagementResult.Failure(CleanArgumentMessage(exception));
        }
    }

    private async Task<Doctor> RequireDoctorAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await management.GetDoctorRecordAsync(doctorId, cancellationToken)
        ?? throw new InvalidOperationException("The doctor was not found.");

    /// <summary>
    /// A window written before history existed has no snapshot. Its pre-change state is captured here as a baseline
    /// (effective from <see cref="DateOnly.MinValue"/>) so past analytics keep the times it really had. The baseline is
    /// stored only by <see cref="RecordHistoryAsync"/>, after every check passes.
    /// </summary>
    private async Task<DoctorWorkingHourSnapshot?> CaptureBaselineIfLegacyAsync(DoctorWorkingHour window, CancellationToken cancellationToken) =>
        await management.HasSnapshotAsync(window.Id, cancellationToken)
            ? null
            : DoctorWorkingHourSnapshot.Capture(window, DateOnly.MinValue, clock.UtcNow);

    /// <summary>Stores the optional baseline, then the window's state from today. Call only on a successful path.</summary>
    private async Task RecordHistoryAsync(DoctorWorkingHour window, DoctorWorkingHourSnapshot? baseline, CancellationToken cancellationToken)
    {
        if (baseline is not null)
            await management.AddSnapshotAsync(baseline, cancellationToken);
        await management.AddSnapshotAsync(DoctorWorkingHourSnapshot.Capture(window, clock.Today, clock.UtcNow), cancellationToken);
    }

    private static DoctorWorkingHour FindWindow(IReadOnlyList<DoctorWorkingHour> windows, Guid workingWindowId) =>
        windows.FirstOrDefault(item => item.Id == workingWindowId)
        ?? throw new InvalidOperationException("The working window was not found.");

    private static void EnsureNoActiveOverlap(IReadOnlyList<DoctorWorkingHour> windows, DayOfWeek day, TimeOnly start, TimeOnly end, Guid? exceptWindowId)
    {
        var overlapping = windows.FirstOrDefault(item =>
            item.IsActive && item.DayOfWeek == day && item.Id != exceptWindowId &&
            DoctorWorkingHour.Overlaps(item.StartTime, item.EndTime, start, end));
        if (overlapping is not null)
            throw new InvalidOperationException(
                $"This window overlaps an active working window on {day} ({overlapping.StartTime:h\\:mm tt}-{overlapping.EndTime:h\\:mm tt}).");
    }

    /// <summary>Refuses when a live (Pending or Approved) extension still points at a window end that is about to disappear or change.</summary>
    private async Task EnsureNoLiveExtensionsForEndAsync(Guid doctorId, TimeOnly endTime, CancellationToken cancellationToken)
    {
        var referencing = (await schedule.GetForDoctorFromDateAsync(doctorId, clock.Today, cancellationToken))
            .Count(item => item.OriginalEndTime == endTime &&
                           item.Status is WorkingHourExtensionStatus.Pending or WorkingHourExtensionStatus.Approved);
        if (referencing > 0)
            throw new InvalidOperationException(
                $"{referencing} working-hour extension(s) use the window end {endTime:h\\:mm tt}. Cancel or resolve them before changing this window.");
    }

    /// <summary>
    /// Every future Pending or Confirmed appointment must sit inside one active window (after approved extensions) and
    /// lie on the appointment grid of the doctor's duration. Otherwise the change is refused and staff reschedule first.
    /// </summary>
    private async Task EnsureFutureAppointmentsFitAsync(Doctor doctor, IReadOnlyList<DoctorWorkingHour> windows, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var live = await management.GetLiveAppointmentsAsync(doctor.Id, today, DateOnly.MaxValue, cancellationToken);
        if (live.Count == 0)
            return;

        var activeWindows = windows.Where(item => item.IsActive).ToArray();
        var approved = (await schedule.GetForDoctorFromDateAsync(doctor.Id, today, cancellationToken))
            .Where(item => item.Status == WorkingHourExtensionStatus.Approved)
            .ToArray();
        // Each appointment is checked against the length in force on its own day.
        var problems = live.Where(appointment =>
            !FitsSchedule(appointment, activeWindows, approved, TimeSpan.FromMinutes(doctor.DurationOn(appointment.Date)))).ToArray();
        if (problems.Length > 0)
        {
            var first = problems[0];
            throw new InvalidOperationException(
                $"{problems.Length} booked appointment(s) would fall outside the new schedule or no longer fit the appointment grid " +
                $"(first: {first.Date:yyyy-MM-dd} {first.Start:h\\:mm tt}). Reschedule or cancel those appointments first.");
        }
    }

    private static bool FitsSchedule(LiveAppointmentFact appointment, IReadOnlyList<DoctorWorkingHour> activeWindows, IReadOnlyList<DoctorWorkingHourExtension> approved, TimeSpan grid)
    {
        var length = appointment.End.ToTimeSpan() - appointment.Start.ToTimeSpan();
        if (length != grid)
            return false;

        return activeWindows.Any(window =>
        {
            if (window.DayOfWeek != appointment.Date.DayOfWeek)
                return false;
            var effectiveEnd = approved.FirstOrDefault(item => item.Date == appointment.Date && item.OriginalEndTime == window.EndTime)?.ExtendedEndTime
                               ?? window.EndTime;
            if (appointment.Start < window.StartTime || appointment.End > effectiveEnd)
                return false;
            var offset = appointment.Start.ToTimeSpan() - window.StartTime.ToTimeSpan();
            return offset.Ticks % grid.Ticks == 0;
        });
    }

    /// <summary>
    /// Brings every booked day from tomorrow to the last live appointment onto the grid of its own duration. A day that
    /// already fits is skipped. Nothing is written until every day has been planned, so a refusal leaves no partial change.
    /// Notifications are returned, not published: the caller publishes them after the transaction commits.
    /// </summary>
    private async Task<ReconcileSummary> ReconcileAsync(Doctor doctor, ScheduleActor actor, CancellationToken cancellationToken)
    {
        var from = clock.Today.AddDays(1);
        var live = await appointments.GetLiveForDoctorFromDateAsync(doctor.Id, from, cancellationToken);
        if (live.Count == 0)
            return new ReconcileSummary(0, 0, 0, []);

        var approved = (await schedule.GetForDoctorFromDateAsync(doctor.Id, from, cancellationToken))
            .Where(item => item.Status == WorkingHourExtensionStatus.Approved)
            .ToArray();

        var plans = new List<DayPlan>();
        foreach (var group in live.GroupBy(item => item.AppointmentDate).OrderBy(item => item.Key))
        {
            var plan = await PlanDayAsync(doctor, group.Key, group.OrderBy(item => item.StartTime).ToArray(), approved, cancellationToken);
            if (plan is not null)
                plans.Add(plan);
        }

        var now = clock.UtcNow;
        var moved = 0;
        var daysMoved = 0;
        var daysExtended = 0;
        var notifications = new List<PatientNotification>();
        foreach (var plan in plans)
        {
            if (plan.ExtendWindow is { } window && plan.ExtendTo is { } newEnd)
            {
                var existing = approved.FirstOrDefault(item => item.Date == plan.Date && item.OriginalEndTime == window.ConfiguredEndTime);
                if (existing is null)
                    await schedule.AddExtensionAsync(DoctorWorkingHourExtension.CreateApprovedForDurationChange(
                        doctor.Id, plan.Date, window.ConfiguredEndTime, newEnd, actor.UserId, now), cancellationToken);
                else
                    existing.ExtendTo(newEnd, now);
                daysExtended++;
            }

            // Safe move order. The filtered unique index on (DoctorId, AppointmentDate, StartTime) for Pending and Confirmed
            // rows rejects a row whose new start equals another live row's start. A move later in the day can land on the
            // start of a row that has not moved yet, so the later moves go first, latest original start first. The earlier
            // moves follow, earliest original start first. Rows whose start stays put (only the end moves) go last.
            var later = plan.Moves.Where(move => move.NewStart > move.OldStart).OrderByDescending(move => move.OldStart);
            var earlier = plan.Moves.Where(move => move.NewStart < move.OldStart).OrderBy(move => move.OldStart);
            var endOnly = plan.Moves.Where(move => move.NewStart == move.OldStart);
            foreach (var move in later.Concat(earlier).Concat(endOnly))
            {
                move.Appointment.RetimeForDurationChange(move.NewStart, move.NewEnd, clock.Today, now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (plan.Moves.Count > 0)
                daysMoved++;
            moved += plan.Moves.Count;
            foreach (var move in plan.Moves.Where(item => item.NewStart != item.OldStart))
            {
                notifications.Add(new PatientNotification(
                    move.Appointment.PatientId,
                    PatientNotificationKind.AppointmentRescheduledByClinic,
                    "Appointment time changed",
                    $"Your appointment on {plan.Date:yyyy-MM-dd} moves from {move.OldStart:h\\:mm tt} to {move.NewStart:h\\:mm tt} " +
                    "because the doctor's appointment length changed.",
                    move.Appointment.Id));
            }
        }

        return new ReconcileSummary(moved, daysMoved, daysExtended, notifications);
    }

    /// <summary>Plans one day. Returns null when every live appointment already has the day's length and sits on its grid.</summary>
    private async Task<DayPlan?> PlanDayAsync(
        Doctor doctor,
        DateOnly date,
        IReadOnlyList<Appointment> dayAppointments,
        IReadOnlyList<DoctorWorkingHourExtension> approved,
        CancellationToken cancellationToken)
    {
        var duration = TimeSpan.FromMinutes(doctor.DurationOn(date));
        var windows = await availability.GetEffectiveWindowsAsync(doctor.Id, date, cancellationToken);
        var breaks = await schedule.GetBreaksAsync(doctor.Id, date, cancellationToken);
        var grid = BuildGrid(windows, breaks, duration);

        var fits = dayAppointments.All(item =>
            item.EndTime.ToTimeSpan() - item.StartTime.ToTimeSpan() == duration && grid.Contains(item.StartTime.ToTimeSpan()));
        if (fits)
            return null;

        if ((await disruptions.GetOpenItemsForDoctorAndDateAsync(doctor.Id, date, cancellationToken)).Count > 0)
            throw new InvalidOperationException(
                $"{date:yyyy-MM-dd} has an open doctor disruption. Resolve it before changing the appointment length.");
        if (windows.Count == 0)
            throw new InvalidOperationException(
                $"{date:yyyy-MM-dd} has no working window to hold {dayAppointments.Count} booked appointment(s).");

        var slots = new List<TimeSpan>(grid);
        EffectiveWorkingWindow? extendWindow = null;
        TimeOnly? extendTo = null;
        var overflowNeeded = dayAppointments.Count - grid.Count;
        if (overflowNeeded > 0)
        {
            // The grid ran out. Continue the last window's grid past its end, on the same alignment.
            var last = windows[windows.Count - 1];
            var lastEnd = last.EndTime.ToTimeSpan();
            var candidate = last.StartTime.ToTimeSpan();
            while (candidate + duration <= lastEnd)
                candidate += duration;

            var laterStarts = windows.Where(item => item.StartTime > last.StartTime).Select(item => item.StartTime.ToTimeSpan()).ToArray();
            var overflow = new List<TimeSpan>();
            while (overflow.Count < overflowNeeded)
            {
                var end = candidate + duration;
                if (end > LatestOverflowEnd)
                    throw new InvalidOperationException(
                        $"{date:yyyy-MM-dd} would need working hours past 11:55 PM to hold its appointments. Reschedule some of them first.");
                if (laterStarts.Any(start => start < end))
                    throw new InvalidOperationException(
                        $"{date:yyyy-MM-dd} would need the working hours to reach the next working window. Reschedule some appointments first.");
                if (!IsBlockedByBreak(breaks, candidate, end))
                    overflow.Add(candidate);
                candidate += duration;
            }

            slots.AddRange(overflow);
            extendWindow = last;
            extendTo = TimeOnly.FromTimeSpan(overflow[^1] + duration);
        }

        var chosen = AssignInOrder(dayAppointments, slots);
        var moves = new List<PlannedMove>();
        for (var k = 0; k < dayAppointments.Count; k++)
        {
            var appointment = dayAppointments[k];
            var newStart = TimeOnly.FromTimeSpan(slots[chosen[k]]);
            var newEnd = TimeOnly.FromTimeSpan(slots[chosen[k]] + duration);
            if (newStart != appointment.StartTime || newEnd != appointment.EndTime)
                moves.Add(new PlannedMove(appointment, appointment.StartTime, newStart, newEnd));
        }

        var bookedIds = dayAppointments.Select(item => item.Id).ToArray();
        foreach (var move in moves)
        {
            if (await appointments.HasLivePatientOverlapAsync(move.Appointment.PatientId, date, move.NewStart, move.NewEnd, bookedIds, cancellationToken))
                throw new InvalidOperationException(
                    $"A patient booked on {date:yyyy-MM-dd} with another doctor would overlap the new time " +
                    $"{move.NewStart:h\\:mm tt} to {move.NewEnd:h\\:mm tt}. Move that booking first.");
        }

        if (moves.Count == 0 && extendWindow is null)
            return null;
        return new DayPlan(date, moves, extendWindow, extendTo);
    }

    /// <summary>
    /// The k-th appointment gets a slot strictly after the (k-1)-th. It takes the free slot nearest its original start
    /// (tie: the earlier slot), but never one that would leave too few slots for the appointments still to place.
    /// </summary>
    private static int[] AssignInOrder(IReadOnlyList<Appointment> items, IReadOnlyList<TimeSpan> slots)
    {
        var chosen = new int[items.Count];
        var previous = -1;
        for (var k = 0; k < items.Count; k++)
        {
            var lowest = previous + 1;
            var highest = slots.Count - 1 - (items.Count - 1 - k);
            var origin = items[k].StartTime.ToTimeSpan();
            var best = -1;
            var bestDistance = TimeSpan.MaxValue;
            for (var index = lowest; index <= highest; index++)
            {
                var distance = (slots[index] - origin).Duration();
                if (distance < bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }

            if (best < 0)
                throw new InvalidOperationException("The day does not have enough slots for its booked appointments.");
            chosen[k] = best;
            previous = best;
        }

        return chosen;
    }

    /// <summary>The slots <see cref="DoctorAvailabilityService"/> would offer on the day, ignoring bookings: aligned starts inside each effective window, minus dated breaks.</summary>
    private static List<TimeSpan> BuildGrid(IReadOnlyList<EffectiveWorkingWindow> windows, IReadOnlyList<DoctorBreak> breaks, TimeSpan duration)
    {
        var grid = new List<TimeSpan>();
        foreach (var window in windows)
        {
            var end = window.EndTime.ToTimeSpan();
            for (var start = window.StartTime.ToTimeSpan(); start + duration <= end; start += duration)
            {
                if (!IsBlockedByBreak(breaks, start, start + duration))
                    grid.Add(start);
            }
        }

        return grid;
    }

    private static bool IsBlockedByBreak(IReadOnlyList<DoctorBreak> breaks, TimeSpan start, TimeSpan end) =>
        breaks.Any(item => item.StartTime.ToTimeSpan() < end && item.EndTime.ToTimeSpan() > start);

    /// <summary>Runs a schedule change in one serializable transaction, then publishes its patient notifications.</summary>
    private async Task<ManagementResult> ChangeWithNotificationsAsync(ScheduleActor actor, Guid doctorId, Func<CancellationToken, Task<ScheduleChangeOutcome>> change, CancellationToken cancellationToken)
    {
        try
        {
            await AuthorizeAsync(actor, doctorId, cancellationToken);
            var outcome = await unitOfWork.ExecuteSerializableWithRetryAsync(change, cancellationToken);
            // Published only after the commit. The retry wrapper may run the change more than once, so nothing is sent inside it.
            foreach (var notification in outcome.Notifications)
                await _notifications.PublishAsync(notification, cancellationToken);
            return ManagementResult.Success(outcome.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ManagementResult.Failure(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ManagementResult.Failure(CleanArgumentMessage(exception));
        }
    }

    private sealed record PlannedMove(Appointment Appointment, TimeOnly OldStart, TimeOnly NewStart, TimeOnly NewEnd);

    private sealed record DayPlan(DateOnly Date, IReadOnlyList<PlannedMove> Moves, EffectiveWorkingWindow? ExtendWindow, TimeOnly? ExtendTo);

    private sealed record ReconcileSummary(int AppointmentsMoved, int DaysMoved, int DaysExtended, List<PatientNotification> Notifications)
    {
        public string Describe() =>
            $"{AppointmentsMoved} appointments moved on {DaysMoved} days, {DaysExtended} days extended, {Notifications.Count} patients notified.";
    }

    private sealed record ScheduleChangeOutcome(string Message, IReadOnlyList<PatientNotification> Notifications);

    /// <summary>A break or holiday counts as upcoming while its date is later than today, or today and not yet ended.</summary>
    private static bool IsUpcoming(DateOnly date, TimeOnly endTime, DateOnly today, TimeOnly now) =>
        date > today || (date == today && endTime > now);

    private static int MondayFirst(DayOfWeek day) => ((int)day + 6) % 7;

    private static string CleanArgumentMessage(ArgumentException exception)
    {
        var message = exception.Message;
        var index = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return index > 0 ? message[..index] : message;
    }
}
