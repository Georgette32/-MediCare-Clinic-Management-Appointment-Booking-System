using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class DoctorScheduleService(
    IAppointmentRepository appointments,
    IDoctorScheduleRepository schedule,
    IDoctorScheduleManagementRepository management,
    IDoctorAvailabilityService availability,
    IUnitOfWork unitOfWork,
    IClinicClock clock) : IDoctorScheduleService
{
    public const int BreakHorizonDays = 60;
    private const int MaxHolidayReasonLength = 500;

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
            doctor.Id, doctor.FullName, doctor.Specialization?.Name ?? string.Empty, doctor.AppointmentDurationMinutes, doctor.Biography,
            doctor.IsActive, today, windows, breaks, holidays);
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
                    $"A working window already starts at {startTime:HH\\:mm} on {day}. Reactivate or edit that window instead.");

            await management.AddWorkingHourAsync(window, ct);
            return $"Working window added: {day} {startTime:HH\\:mm}-{endTime:HH\\:mm}.";
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

            if (windows.Any(item => item.Id != window.Id && item.DayOfWeek == window.DayOfWeek && item.StartTime == startTime))
                throw new InvalidOperationException($"Another working window already starts at {startTime:HH\\:mm} on {window.DayOfWeek}.");
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

            return $"Working window updated: {window.DayOfWeek} {startTime:HH\\:mm}-{endTime:HH\\:mm}.";
        }, cancellationToken);

    public Task<ManagementResult> SetWorkingWindowActiveAsync(ScheduleActor actor, Guid doctorId, Guid workingWindowId, bool isActive, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            var windows = await management.GetAllWorkingHoursAsync(doctorId, ct);
            var window = FindWindow(windows, workingWindowId);
            if (window.IsActive == isActive)
                return $"The working window is already {(isActive ? "active" : "inactive")}.";

            if (isActive)
            {
                EnsureNoActiveOverlap(windows, window.DayOfWeek, window.StartTime, window.EndTime, exceptWindowId: window.Id);
                window.Activate();
                return "Working window reactivated.";
            }

            window.Deactivate();
            await EnsureNoLiveExtensionsForEndAsync(doctorId, window.EndTime, ct);
            await EnsureFutureAppointmentsFitAsync(doctor, windows, ct);
            return "Working window deactivated.";
        }, cancellationToken);

    public Task<ManagementResult> ChangeAppointmentDurationAsync(ScheduleActor actor, Guid doctorId, int minutes, CancellationToken cancellationToken) =>
        ChangeAsync(actor, doctorId, async ct =>
        {
            var doctor = await RequireDoctorAsync(doctorId, ct);
            var windows = await management.GetAllWorkingHoursAsync(doctorId, ct);
            doctor.ChangeAppointmentDuration(minutes);
            await EnsureFutureAppointmentsFitAsync(doctor, windows, ct);
            return $"Appointment duration set to {minutes} minutes.";
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
                    $"This break overlaps another break that day ({overlappingBreak.StartTime:HH\\:mm}-{overlappingBreak.EndTime:HH\\:mm}).");

            var booked = (await management.GetLiveAppointmentsAsync(doctorId, date, date, ct))
                .FirstOrDefault(appointment => appointment.Start < endTime && appointment.End > startTime);
            if (booked is not null)
                throw new InvalidOperationException(
                    $"A patient is booked from {booked.Start:HH\\:mm} to {booked.End:HH\\:mm}, which overlaps this break. Reschedule that appointment first.");

            await management.AddBreakAsync(item, ct);
            return $"Break added: {date:yyyy-MM-dd} {startTime:HH\\:mm}-{endTime:HH\\:mm}.";
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
                $"This window overlaps an active working window on {day} ({overlapping.StartTime:HH\\:mm}-{overlapping.EndTime:HH\\:mm}).");
    }

    /// <summary>Refuses when a live (Pending or Approved) extension still points at a window end that is about to disappear or change.</summary>
    private async Task EnsureNoLiveExtensionsForEndAsync(Guid doctorId, TimeOnly endTime, CancellationToken cancellationToken)
    {
        var referencing = (await schedule.GetForDoctorFromDateAsync(doctorId, clock.Today, cancellationToken))
            .Count(item => item.OriginalEndTime == endTime &&
                           item.Status is WorkingHourExtensionStatus.Pending or WorkingHourExtensionStatus.Approved);
        if (referencing > 0)
            throw new InvalidOperationException(
                $"{referencing} working-hour extension(s) use the window end {endTime:HH\\:mm}. Cancel or resolve them before changing this window.");
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
        var grid = TimeSpan.FromMinutes(doctor.AppointmentDurationMinutes);

        var problems = live.Where(appointment => !FitsSchedule(appointment, activeWindows, approved, grid)).ToArray();
        if (problems.Length > 0)
        {
            var first = problems[0];
            throw new InvalidOperationException(
                $"{problems.Length} booked appointment(s) would fall outside the new schedule or no longer fit the appointment grid " +
                $"(first: {first.Date:yyyy-MM-dd} {first.Start:HH\\:mm}). Reschedule or cancel those appointments first.");
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
