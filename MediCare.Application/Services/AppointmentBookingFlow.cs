using MediCare.Application.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Result of a booking, reschedule or clinic booking attempt. On a rule failure the message is the domain's message and
/// <see cref="Alternatives"/> lists the doctor's other free start times on the requested date.
/// </summary>
public sealed record BookingOutcome(bool Succeeded, Guid? AppointmentId, string? Error, IReadOnlyList<TimeOnly> Alternatives)
{
    public static BookingOutcome Success(Guid appointmentId) => new(true, appointmentId, null, []);
    public static BookingOutcome Failure(string error, IReadOnlyList<TimeOnly> alternatives) => new(false, null, error, alternatives);
}

/// <summary>
/// Web-facing booking commands. Each command delegates to <see cref="AppointmentService"/> (the only place the rules live)
/// and turns an expected rule refusal into a <see cref="BookingOutcome"/> with alternatives. Ownership failures
/// (UnauthorizedAccessException) are not caught here.
/// </summary>
public interface IAppointmentBookingFlow
{
    Task<BookingOutcome> RequestForPatientAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, CancellationToken cancellationToken);
    Task<BookingOutcome> RescheduleForPatientAsync(Guid appointmentId, Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, CancellationToken cancellationToken);
    Task<BookingOutcome> BookForClinicAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, ClinicCancellationActor actor, CancellationToken cancellationToken);
    Task<BookingOutcome> RescheduleByClinicAsync(Guid appointmentId, Guid doctorId, DateOnly date, TimeOnly startTime, ClinicCancellationActor actor, CancellationToken cancellationToken);
}

public sealed class AppointmentBookingFlow(AppointmentService appointments, IDoctorAvailabilityService availability) : IAppointmentBookingFlow
{
    public Task<BookingOutcome> RequestForPatientAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, CancellationToken cancellationToken) =>
        Attempt(doctorId, date, startTime, async ct => BookingOutcome.Success(await appointments.RequestAsync(patientId, doctorId, date, startTime, ct)), cancellationToken);

    public Task<BookingOutcome> RescheduleForPatientAsync(Guid appointmentId, Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, CancellationToken cancellationToken) =>
        Attempt(doctorId, date, startTime, async ct => BookingOutcome.Success(await appointments.RescheduleAsync(appointmentId, patientId, doctorId, date, startTime, ct)), cancellationToken);

    public Task<BookingOutcome> BookForClinicAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, ClinicCancellationActor actor, CancellationToken cancellationToken) =>
        Attempt(doctorId, date, startTime, async ct => BookingOutcome.Success(await appointments.BookByClinicAsync(patientId, doctorId, date, startTime, actor, ct)), cancellationToken);

    public Task<BookingOutcome> RescheduleByClinicAsync(Guid appointmentId, Guid doctorId, DateOnly date, TimeOnly startTime, ClinicCancellationActor actor, CancellationToken cancellationToken) =>
        Attempt(doctorId, date, startTime, async ct => BookingOutcome.Success(await appointments.RescheduleByClinicAsync(appointmentId, actor, doctorId, date, startTime, ct)), cancellationToken);

    private async Task<BookingOutcome> Attempt(
        Guid doctorId,
        DateOnly date,
        TimeOnly startTime,
        Func<CancellationToken, Task<BookingOutcome>> command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await command(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BookingOutcome.Failure(exception.Message, await AlternativesAsync(doctorId, date, startTime, cancellationToken));
        }
    }

    private async Task<IReadOnlyList<TimeOnly>> AlternativesAsync(Guid doctorId, DateOnly date, TimeOnly failedStart, CancellationToken cancellationToken)
    {
        try
        {
            var slots = await availability.GetAvailableSlotsAsync(doctorId, date, cancellationToken);
            return slots.Where(slot => slot != failedStart).ToArray();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
