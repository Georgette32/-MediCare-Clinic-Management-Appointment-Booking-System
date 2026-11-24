namespace MediCare.Application.Abstractions;

/// <summary>A Confirmed appointment that is due for a reminder.</summary>
public sealed record ReminderCandidate(Guid AppointmentId, Guid PatientId, DateOnly AppointmentDate, TimeOnly StartTime, string DoctorName);

public interface IAppointmentReminderRepository
{
    /// <summary>Confirmed appointments on the given clinic-local date, oldest start first.</summary>
    Task<IReadOnlyList<ReminderCandidate>> GetConfirmedForDateAsync(DateOnly appointmentDate, CancellationToken cancellationToken);
}
