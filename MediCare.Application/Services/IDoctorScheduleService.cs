namespace MediCare.Application.Services;

/// <summary>Who is making a schedule request. Doctors may act only on their own profile; Admins may act on any doctor.</summary>
public sealed record ScheduleActor(Guid UserId, bool IsAdmin);

public sealed record ManagementResult(bool Succeeded, string Message)
{
    public static ManagementResult Success(string message) => new(true, message);
    public static ManagementResult Failure(string message) => new(false, message);
}

public sealed record WorkingWindowItem(Guid Id, DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime, bool IsActive);

public sealed record DoctorBreakItem(Guid Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, bool IsDisruption, bool CanRemove);

public sealed record DoctorHolidayItem(Guid Id, DateOnly StartDate, DateOnly EndDate, string? Reason, bool CanRemove);

public sealed record DoctorScheduleView(
    Guid DoctorId,
    string FullName,
    string Specialization,
    int AppointmentDurationMinutes,
    string? Biography,
    bool IsActive,
    DateOnly Today,
    IReadOnlyList<WorkingWindowItem> WorkingWindows,
    IReadOnlyList<DoctorBreakItem> Breaks,
    IReadOnlyList<DoctorHolidayItem> Holidays);

public interface IDoctorScheduleService
{
    /// <summary>The active doctor profile linked to the account, or null.</summary>
    Task<Guid?> GetOwnDoctorIdAsync(Guid applicationUserId, CancellationToken cancellationToken);

    Task<DoctorScheduleView> GetScheduleAsync(ScheduleActor actor, Guid doctorId, CancellationToken cancellationToken);

    Task<ManagementResult> AddWorkingWindowAsync(ScheduleActor actor, Guid doctorId, DayOfWeek day, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken);
    Task<ManagementResult> ChangeWorkingWindowTimesAsync(ScheduleActor actor, Guid doctorId, Guid workingWindowId, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken);
    Task<ManagementResult> SetWorkingWindowActiveAsync(ScheduleActor actor, Guid doctorId, Guid workingWindowId, bool isActive, CancellationToken cancellationToken);

    Task<ManagementResult> ChangeAppointmentDurationAsync(ScheduleActor actor, Guid doctorId, int minutes, CancellationToken cancellationToken);

    Task<ManagementResult> AddBreakAsync(ScheduleActor actor, Guid doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken);
    Task<ManagementResult> RemoveBreakAsync(ScheduleActor actor, Guid doctorId, Guid breakId, CancellationToken cancellationToken);

    Task<ManagementResult> AddHolidayAsync(ScheduleActor actor, Guid doctorId, DateOnly startDate, DateOnly endDate, string? reason, CancellationToken cancellationToken);
    Task<ManagementResult> RemoveHolidayAsync(ScheduleActor actor, Guid doctorId, Guid holidayId, CancellationToken cancellationToken);

    Task<ManagementResult> UpdateBiographyAsync(ScheduleActor actor, Guid doctorId, string? biography, CancellationToken cancellationToken);
}
