namespace MediCare.Application.Services;

public interface ICheckInService
{
    Task<ScheduledCheckInResult> CheckInScheduledPatientAsync(Guid appointmentId, Guid expectedDoctorId, CancellationToken cancellationToken);
    Task<Guid> RegisterWalkInAsync(Guid patientId, Guid doctorId, CancellationToken cancellationToken);
}

public sealed record ScheduledCheckInResult(Guid? QueueEntryId, bool RequiresWalkInRegistration, string? Message);

public interface ICheckInWarningProvider
{
    Task<string?> GetWarningAsync(Guid appointmentId, CancellationToken cancellationToken);
}
