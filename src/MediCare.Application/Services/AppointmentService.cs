using System.Data;
using MediCare.Application.DTOs.Scheduling;
using MediCare.Application.Enums;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

/// <summary>
/// Booking workflow for appointment requests, confirmations, cancellations and
/// rescheduling. The shared Appointment entity is extended rather than replaced so
/// dashboards, invoices and notifications keep using the same aggregate.
/// </summary>
public sealed class AppointmentService(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock)
{
    public async Task<Guid> RequestAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly date,
        TimeOnly startTime,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var doctor = await GetBookingDoctorAsync(patientProfileId, doctorProfileId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(
            doctor, date, startTime, exceptAppointmentId: null, cancellationToken);

        var appointment = Appointment.Request(
            patientProfileId, doctorProfileId, date, startTime, endTime, clinicClock.UtcNow);

        await appointments.AddAsync(appointment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return appointment.Id;
    }

    public async Task CancelByPatientAsync(
        Guid appointmentId,
        Guid patientProfileId,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        if (appointment.PatientProfileId != patientProfileId)
        {
            throw new UnauthorizedAccessException("Patients can cancel only their own appointments.");
        }

        appointment.CancelByPatient(clinicClock.Today, clinicClock.UtcNow, reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelByClinicAsync(
        Guid appointmentId,
        ClinicCancellationActor actor,
        string reason,
        CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);

        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        appointment.CancelByClinic(reason, clinicClock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> RescheduleAsync(
        Guid appointmentId,
        Guid patientProfileId,
        Guid replacementDoctorProfileId,
        DateOnly newDate,
        TimeOnly newStartTime,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var original = await GetAppointmentAsync(appointmentId, cancellationToken);
        if (original.PatientProfileId != patientProfileId)
        {
            throw new UnauthorizedAccessException("Patients can reschedule only their own appointments.");
        }

        if (original.AppointmentDate <= clinicClock.Today)
        {
            throw new InvalidOperationException(
                "Patients may request rescheduling only before the appointment day.");
        }

        EnsureReplacementUsesDifferentSlot(original, replacementDoctorProfileId, newDate, newStartTime);

        if (await appointments.HasLiveRescheduledSuccessorAsync(appointmentId, cancellationToken))
        {
            throw new InvalidOperationException("This appointment already has a live replacement request.");
        }

        var doctor = await GetDoctorAsync(replacementDoctorProfileId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(
            doctor, newDate, newStartTime, original.Id, cancellationToken);

        var replacement = original.CreateRescheduledReplacement(
            replacementDoctorProfileId, newDate, newStartTime, endTime, clinicClock.UtcNow);

        await appointments.AddAsync(replacement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement.Id;
    }

    public async Task<Guid> RescheduleByClinicAsync(
        Guid appointmentId,
        ClinicCancellationActor actor,
        Guid replacementDoctorProfileId,
        DateOnly newDate,
        TimeOnly newStartTime,
        CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);

        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var original = await GetAppointmentAsync(appointmentId, cancellationToken);
        EnsureReplacementUsesDifferentSlot(original, replacementDoctorProfileId, newDate, newStartTime);

        if (await appointments.HasLiveRescheduledSuccessorAsync(appointmentId, cancellationToken))
        {
            throw new InvalidOperationException("This appointment already has a live replacement request.");
        }

        var doctor = await GetDoctorAsync(replacementDoctorProfileId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(
            doctor, newDate, newStartTime, original.Id, cancellationToken);

        var replacement = original.ReplaceByClinic(
            replacementDoctorProfileId, newDate, newStartTime, endTime, clinicClock.UtcNow);

        await appointments.AddAsync(replacement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement.Id;
    }

    public async Task ConfirmAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        var now = clinicClock.UtcNow;
        appointment.Confirm(now);

        if (appointment.RescheduledFromAppointmentId is Guid originalAppointmentId)
        {
            var original = await GetAppointmentAsync(originalAppointmentId, cancellationToken);
            original.MarkRescheduled(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteByDoctorAsync(
        Guid appointmentId,
        Guid doctorUserId,
        string? doctorNotes,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var doctor = await appointments.GetDoctorProfileByUserIdAsync(doctorUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "The authenticated account is not linked to an active doctor profile.");

        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        if (appointment.DoctorProfileId != doctor.Id)
        {
            throw new UnauthorizedAccessException(
                "Doctors can complete only their own appointments.");
        }

        appointment.Complete(doctorNotes);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task RejectAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        UpdatePendingAppointmentAsync(
            appointmentId,
            (appointment, now) => appointment.Reject(now),
            cancellationToken);

    public Task<IReadOnlyList<PendingAppointmentDto>> GetPendingAsync(
        CancellationToken cancellationToken) =>
        appointments.GetPendingWithNamesAsync(cancellationToken);

    private async Task UpdatePendingAppointmentAsync(
        Guid appointmentId,
        Action<Appointment, DateTime> update,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        update(appointment, clinicClock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<DoctorProfile> GetBookingDoctorAsync(
        Guid patientProfileId,
        Guid doctorProfileId,
        CancellationToken cancellationToken)
    {
        if (!await appointments.PatientExistsAsync(patientProfileId, cancellationToken))
        {
            throw new InvalidOperationException("The patient was not found.");
        }

        return await GetDoctorAsync(doctorProfileId, cancellationToken);
    }

    private async Task<DoctorProfile> GetDoctorAsync(
        Guid doctorProfileId,
        CancellationToken cancellationToken) =>
        await appointments.GetDoctorProfileAsync(doctorProfileId, cancellationToken)
        ?? throw new InvalidOperationException("The active doctor was not found.");

    private async Task<Appointment> GetAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        await appointments.GetByIdAsync(appointmentId, cancellationToken)
        ?? throw new InvalidOperationException("The appointment was not found.");

    private static void EnsureClinicActor(ClinicCancellationActor actor)
    {
        if (actor is not (ClinicCancellationActor.Reception or ClinicCancellationActor.Admin))
        {
            throw new UnauthorizedAccessException(
                "Only Reception or Admin can perform clinic cancellation or rescheduling.");
        }
    }

    private static void EnsureReplacementUsesDifferentSlot(
        Appointment original,
        Guid replacementDoctorProfileId,
        DateOnly newDate,
        TimeOnly newStartTime)
    {
        if (original.DoctorProfileId == replacementDoctorProfileId &&
            original.AppointmentDate == newDate &&
            original.StartTime == newStartTime)
        {
            throw new InvalidOperationException(
                "A replacement must use a different doctor, appointment date, or start time.");
        }
    }

    private async Task<TimeOnly> ValidateAndGetEndTimeAsync(
        DoctorProfile doctor,
        DateOnly date,
        TimeOnly startTime,
        Guid? exceptAppointmentId,
        CancellationToken cancellationToken)
    {
        var schedules = await appointments.GetWorkingHoursAsync(
            doctor.Id, date.DayOfWeek, cancellationToken);

        var matchingSchedules = schedules
            .Where(item => item.IsActive && startTime >= item.StartTime && startTime < item.EndTime)
            .ToArray();

        if (matchingSchedules.Length == 0)
        {
            throw new InvalidOperationException(
                "The requested time is outside the doctor's configured working schedule.");
        }

        if (matchingSchedules.Length > 1)
        {
            throw new InvalidOperationException(
                "The doctor's working schedule contains overlapping entries. Resolve the schedule before accepting bookings.");
        }

        var schedule = matchingSchedules[0];
        var duration = TimeSpan.FromMinutes(doctor.AppointmentDurationMinutes);
        var elapsedTicks = (startTime - schedule.StartTime).Ticks;
        if (elapsedTicks % duration.Ticks != 0)
        {
            throw new InvalidOperationException(
                "The requested time does not align with the doctor's configured appointment schedule.");
        }

        var endTime = startTime.Add(duration);
        if (endTime > schedule.EndTime)
        {
            throw new InvalidOperationException(
                "The appointment would extend beyond the doctor's working hours.");
        }

        if (await appointments.IsHolidayAsync(doctor.Id, date, cancellationToken))
        {
            throw new InvalidOperationException("The doctor is unavailable on the selected date.");
        }

        if (await appointments.HasBreakOverlapAsync(doctor.Id, date, startTime, endTime, cancellationToken))
        {
            throw new InvalidOperationException("The appointment overlaps a doctor's break.");
        }

        if (await appointments.HasAppointmentConflictAsync(
                doctor.Id, date, startTime, endTime, exceptAppointmentId, cancellationToken))
        {
            throw new InvalidOperationException("The requested appointment time is already reserved.");
        }

        return endTime;
    }
}
