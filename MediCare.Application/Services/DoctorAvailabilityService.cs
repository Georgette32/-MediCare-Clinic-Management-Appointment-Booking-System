using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

/// <summary>
/// The one availability source for booking, rescheduling, walk-in Call and the booking UI.
/// Working windows come from DoctorWorkingHour; an Approved extension replaces the end of the window it was recorded
/// against. Holidays remove every window for the date. Breaks and booked appointments are separate blocked intervals.
/// </summary>
public interface IDoctorAvailabilityService
{
    /// <summary>Active windows for the doctor's weekday with approved extensions applied. Empty on holiday dates.</summary>
    Task<IReadOnlyList<EffectiveWorkingWindow>> GetEffectiveWindowsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// Free, aligned start times inside the effective windows that do not overlap a break or a Pending/Confirmed
    /// appointment. On today's date, starts before the current clinic time are excluded. Past dates return nothing.
    /// </summary>
    Task<IReadOnlyList<TimeOnly>> GetAvailableSlotsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>A working window after approved extensions. <see cref="ExtensionId"/> is set when its end was extended.</summary>
public sealed record EffectiveWorkingWindow(TimeOnly StartTime, TimeOnly EndTime, TimeOnly ConfiguredEndTime, Guid? ExtensionId);

public sealed class DoctorAvailabilityService(
    IAppointmentRepository appointments,
    IDoctorScheduleRepository schedule,
    IClinicClock clinicClock) : IDoctorAvailabilityService
{
    public async Task<IReadOnlyList<EffectiveWorkingWindow>> GetEffectiveWindowsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        if (await appointments.IsHolidayAsync(doctorId, date, cancellationToken))
            return [];

        var hours = await appointments.GetWorkingHoursAsync(doctorId, date.DayOfWeek, cancellationToken);
        var approved = await schedule.GetApprovedForDoctorAndDateAsync(doctorId, date, cancellationToken);

        return hours
            .OrderBy(hour => hour.StartTime)
            .Select(hour =>
            {
                var extension = approved.FirstOrDefault(item => item.OriginalEndTime == hour.EndTime);
                return extension is null
                    ? new EffectiveWorkingWindow(hour.StartTime, hour.EndTime, hour.EndTime, null)
                    : new EffectiveWorkingWindow(hour.StartTime, extension.ExtendedEndTime, hour.EndTime, extension.Id);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<TimeOnly>> GetAvailableSlotsAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        if (date < today)
            return [];

        var doctor = await appointments.GetDoctorAsync(doctorId, cancellationToken)
            ?? throw new InvalidOperationException("The active doctor was not found.");
        var duration = TimeSpan.FromMinutes(doctor.AppointmentDurationMinutes);
        var windows = await GetEffectiveWindowsAsync(doctorId, date, cancellationToken);
        if (windows.Count == 0)
            return [];

        var breaks = await schedule.GetBreaksAsync(doctorId, date, cancellationToken);
        var booked = await schedule.GetLiveAppointmentIntervalsAsync(doctorId, date, cancellationToken);
        var now = clinicClock.CurrentTime;
        var slots = new List<TimeOnly>();

        foreach (var window in windows)
        {
            var windowEnd = window.EndTime.ToTimeSpan();
            for (var start = window.StartTime.ToTimeSpan(); start + duration <= windowEnd; start += duration)
            {
                var slotStart = TimeOnly.FromTimeSpan(start);
                var slotEnd = slotStart.Add(duration);
                if (date == today && slotStart < now)
                    continue;
                if (breaks.Any(item => item.StartTime < slotEnd && item.EndTime > slotStart))
                    continue;
                if (booked.Any(item => item.Start < slotEnd && item.End > slotStart))
                    continue;

                slots.Add(slotStart);
            }
        }

        return slots;
    }
}
