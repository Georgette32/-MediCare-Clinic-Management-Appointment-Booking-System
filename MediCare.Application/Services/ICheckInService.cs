namespace MediCare.Application.Services;

public interface ICheckInService
{
    Task<ScheduledCheckInResult> CheckInScheduledPatientAsync(Guid appointmentId, Guid expectedDoctorId, CancellationToken cancellationToken);
    Task<Guid> RegisterWalkInAsync(Guid patientId, Guid doctorId, CancellationToken cancellationToken);
}

/// <summary>Warning is set when the check-in succeeded but the appointment needs staff attention (for example a disruption).</summary>
public sealed record ScheduledCheckInResult(Guid? QueueEntryId, bool RequiresWalkInRegistration, string? Message, string? Warning = null);

/// <summary>Returns a staff-facing warning for an appointment, or null. Warnings never block check-in.</summary>
public interface ICheckInWarningProvider
{
    Task<string?> GetWarningAsync(Guid appointmentId, CancellationToken cancellationToken);
}
