using MediCare.Application.Abstractions;

namespace MediCare.Application.Services;

/// <summary>
/// Doctor-level reasons that stop patients being called or new walk-ins being registered on a clinic date.
/// A doctor who left early cannot be called from the disruption's from-time onward; a dated break blocks calls while it lasts.
/// </summary>
public interface IDoctorCallWindowService
{
    /// <summary>Reason a patient cannot be called for the doctor now (left early or on a dated break), or null.</summary>
    Task<string?> GetCallBlockReasonAsync(Guid doctorId, DateOnly date, TimeOnly currentTime, CancellationToken cancellationToken);

    /// <summary>Reason a new walk-in cannot be registered because the doctor has left early today, or null.</summary>
    Task<string?> GetLeftEarlyReasonAsync(Guid doctorId, DateOnly date, TimeOnly currentTime, CancellationToken cancellationToken);
}

public sealed class DoctorCallWindowService(
    IAppointmentRepository appointments,
    IDoctorDisruptionRepository disruptions,
    IDoctorScheduleRepository schedule) : IDoctorCallWindowService
{
    public const string OnBreakMessage = "The doctor is on a scheduled break; patients cannot be called until it ends.";

    public async Task<string?> GetCallBlockReasonAsync(Guid doctorId, DateOnly date, TimeOnly currentTime, CancellationToken cancellationToken)
    {
        var leftEarly = await GetLeftEarlyReasonAsync(doctorId, date, currentTime, cancellationToken);
        if (leftEarly is not null)
            return leftEarly;

        var breaks = await schedule.GetBreaksAsync(doctorId, date, cancellationToken);
        return breaks.Any(item => item.StartTime <= currentTime && currentTime < item.EndTime) ? OnBreakMessage : null;
    }

    public async Task<string?> GetLeftEarlyReasonAsync(Guid doctorId, DateOnly date, TimeOnly currentTime, CancellationToken cancellationToken)
    {
        var disruption = await disruptions.GetForDoctorAndDateAsync(doctorId, date, cancellationToken);
        if (disruption is null || currentTime < disruption.FromTime)
            return null;

        var doctor = await appointments.GetDoctorAsync(doctorId, cancellationToken);
        var name = doctor?.FullName ?? "The doctor";
        return $"{name} left at {disruption.FromTime:HH\\:mm}; use the disruption worklist.";
    }
}
