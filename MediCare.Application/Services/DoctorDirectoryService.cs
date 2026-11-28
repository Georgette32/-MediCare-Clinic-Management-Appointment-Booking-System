using MediCare.Application.Abstractions;

namespace MediCare.Application.Services;

public sealed record DoctorDirectoryItem(
    Guid DoctorId,
    string FullName,
    string Specialization,
    string? BiographyExcerpt,
    int AppointmentDurationMinutes,
    DateOnly? Date,
    int? FreeSlots,
    bool IsHoliday);

public sealed record WeeklyHourItem(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime);

public sealed record HolidayPeriod(DateOnly StartDate, DateOnly EndDate, string? Reason);

public sealed record DoctorProfileView(
    Guid DoctorId,
    string FullName,
    string Specialization,
    string? Biography,
    int AppointmentDurationMinutes,
    DateOnly SelectedDate,
    bool IsHoliday,
    IReadOnlyList<WeeklyHourItem> WeeklyHours,
    IReadOnlyList<HolidayPeriod> UpcomingHolidays,
    IReadOnlyList<TimeOnly> FreeSlots,
    int AppointmentDurationOnDateMinutes);

/// <summary>Doctor discovery for every signed-in role. Only active doctors are listed.</summary>
public interface IDoctorDirectoryService
{
    Task<IReadOnlyList<DoctorDirectoryItem>> SearchAsync(Guid? specializationId, string? nameContains, DateOnly? date, CancellationToken cancellationToken);
    Task<DoctorProfileView?> GetProfileAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
}

public sealed class DoctorDirectoryService(
    IAppointmentRepository appointments,
    IDoctorScheduleManagementRepository management,
    IDoctorAvailabilityService availability,
    IClinicClock clock) : IDoctorDirectoryService
{
    public const int BiographyExcerptLength = 160;

    public async Task<IReadOnlyList<DoctorDirectoryItem>> SearchAsync(Guid? specializationId, string? nameContains, DateOnly? date, CancellationToken cancellationToken)
    {
        var doctors = await management.GetDoctorsWithSpecializationAsync(activeOnly: true, cancellationToken);
        var name = nameContains?.Trim();

        var matches = doctors
            .Where(doctor => specializationId is null || doctor.SpecializationId == specializationId)
            .Where(doctor => string.IsNullOrEmpty(name) || doctor.FullName.Contains(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(doctor => doctor.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = new List<DoctorDirectoryItem>(matches.Length);
        foreach (var doctor in matches)
        {
            int? freeSlots = null;
            var isHoliday = false;
            if (date is DateOnly day)
            {
                isHoliday = await appointments.IsHolidayAsync(doctor.Id, day, cancellationToken);
                if (!isHoliday)
                    freeSlots = (await availability.GetAvailableSlotsAsync(doctor.Id, day, cancellationToken)).Count;
            }

            items.Add(new DoctorDirectoryItem(
                doctor.Id,
                doctor.FullName,
                doctor.Specialization?.Name ?? string.Empty,
                Excerpt(doctor.Biography),
                doctor.DurationOn(clock.Today),
                date,
                freeSlots,
                isHoliday));
        }

        return items;
    }

    public async Task<DoctorProfileView?> GetProfileAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        var doctor = (await management.GetDoctorsWithSpecializationAsync(activeOnly: true, cancellationToken))
            .FirstOrDefault(item => item.Id == doctorId);
        if (doctor is null)
            return null;

        var today = clock.Today;
        var weekly = (await management.GetAllWorkingHoursAsync(doctorId, cancellationToken))
            .Where(item => item.IsActive)
            .OrderBy(item => ((int)item.DayOfWeek + 6) % 7)
            .ThenBy(item => item.StartTime)
            .Select(item => new WeeklyHourItem(item.DayOfWeek, item.StartTime, item.EndTime))
            .ToArray();

        var holidays = (await management.GetHolidaysEndingOnOrAfterAsync(doctorId, today, cancellationToken))
            .OrderBy(item => item.StartDate)
            .Select(item => new HolidayPeriod(item.StartDate, item.EndDate, item.Reason))
            .ToArray();

        var isHoliday = await appointments.IsHolidayAsync(doctorId, date, cancellationToken);
        var slots = isHoliday
            ? []
            : await availability.GetAvailableSlotsAsync(doctorId, date, cancellationToken);

        return new DoctorProfileView(
            doctor.Id,
            doctor.FullName,
            doctor.Specialization?.Name ?? string.Empty,
            doctor.Biography,
            doctor.DurationOn(clock.Today),
            date,
            isHoliday,
            weekly,
            holidays,
            slots,
            doctor.DurationOn(date));
    }

    private static string? Excerpt(string? biography)
    {
        if (string.IsNullOrWhiteSpace(biography))
            return null;
        return biography.Length <= BiographyExcerptLength
            ? biography
            : biography[..BiographyExcerptLength].TrimEnd() + "...";
    }
}
