using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;

namespace MediCare.Application.Services;

public sealed class AppointmentService(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock)
{
    public async Task<Guid> RequestAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var doctor = await GetBookingDoctorAsync(patientId, doctorId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(doctor, date, startTime, exceptAppointmentId: null, cancellationToken);
        var appointment = Appointment.Request(patientId, doctorId, date, startTime, endTime, clinicClock.UtcNow);
        await appointments.AddAsync(appointment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return appointment.Id;
    }

    public async Task CancelByPatientAsync(Guid appointmentId, Guid patientId, string? reason, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        if (appointment.PatientId != patientId)
            throw new UnauthorizedAccessException("Patients can cancel only their own appointments.");

        appointment.CancelByPatient(clinicClock.Today, clinicClock.UtcNow, reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelByClinicAsync(Guid appointmentId, ClinicCancellationActor actor, string reason, CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        appointment.CancelByClinic(reason, clinicClock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> RescheduleAsync(Guid appointmentId, Guid patientId, Guid replacementDoctorId, DateOnly newDate, TimeOnly newStartTime, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var original = await GetAppointmentAsync(appointmentId, cancellationToken);
        if (original.PatientId != patientId)
            throw new UnauthorizedAccessException("Patients can reschedule only their own appointments.");
        if (original.AppointmentDate <= clinicClock.Today)
            throw new InvalidOperationException("Patients may request rescheduling only before the appointment day.");
        EnsureReplacementUsesDifferentSlot(original, replacementDoctorId, newDate, newStartTime);
        if (await appointments.HasLiveRescheduledSuccessorAsync(appointmentId, cancellationToken))
            throw new InvalidOperationException("This appointment already has a live replacement request.");

        var doctor = await GetDoctorAsync(replacementDoctorId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(doctor, newDate, newStartTime, original.Id, cancellationToken);
        var replacement = original.CreateRescheduledReplacement(replacementDoctorId, newDate, newStartTime, endTime, clinicClock.UtcNow);
        await appointments.AddAsync(replacement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement.Id;
    }

    public async Task<Guid> RescheduleByClinicAsync(
        Guid appointmentId,
        ClinicCancellationActor actor,
        Guid replacementDoctorId,
        DateOnly newDate,
        TimeOnly newStartTime,
        CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var original = await GetAppointmentAsync(appointmentId, cancellationToken);
        EnsureReplacementUsesDifferentSlot(original, replacementDoctorId, newDate, newStartTime);
        if (await appointments.HasLiveRescheduledSuccessorAsync(appointmentId, cancellationToken))
            throw new InvalidOperationException("This appointment already has a live replacement request.");

        var doctor = await GetDoctorAsync(replacementDoctorId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(doctor, newDate, newStartTime, original.Id, cancellationToken);
        var replacement = original.ReplaceByClinic(replacementDoctorId, newDate, newStartTime, endTime, clinicClock.UtcNow);
        await appointments.AddAsync(replacement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacement.Id;
    }

    public async Task ConfirmAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
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

    public Task RejectAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        UpdatePendingAppointmentAsync(appointmentId, (appointment, now) => appointment.Reject(now), cancellationToken);

    public async Task<IReadOnlyList<PendingAppointmentDto>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var pendingAppointments = await appointments.GetPendingAsync(cancellationToken);
        return pendingAppointments.Select(item => new PendingAppointmentDto(
            item.Id,
            item.Patient.FullName,
            item.Doctor.FullName,
            item.AppointmentDate,
            item.StartTime,
            item.EndTime)).ToArray();
    }

    private async Task UpdatePendingAppointmentAsync(Guid appointmentId, Action<Appointment, DateTimeOffset> update, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        update(appointment, clinicClock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Doctor> GetBookingDoctorAsync(Guid patientId, Guid doctorId, CancellationToken cancellationToken)
    {
        if (!await appointments.PatientExistsAsync(patientId, cancellationToken))
            throw new InvalidOperationException("The patient was not found.");
        return await GetDoctorAsync(doctorId, cancellationToken);
    }

    private async Task<Doctor> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await appointments.GetDoctorAsync(doctorId, cancellationToken)
        ?? throw new InvalidOperationException("The active doctor was not found.");

    private async Task<Appointment> GetAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        await appointments.GetByIdAsync(appointmentId, cancellationToken)
        ?? throw new InvalidOperationException("The appointment was not found.");

    private static void EnsureClinicActor(ClinicCancellationActor actor)
    {
        if (actor is not (ClinicCancellationActor.Reception or ClinicCancellationActor.Admin))
            throw new UnauthorizedAccessException("Only Reception or Admin can perform clinic cancellation or rescheduling.");
    }

    private static void EnsureReplacementUsesDifferentSlot(Appointment original, Guid replacementDoctorId, DateOnly newDate, TimeOnly newStartTime)
    {
        if (original.DoctorId == replacementDoctorId && original.AppointmentDate == newDate && original.StartTime == newStartTime)
            throw new InvalidOperationException("A replacement must use a different doctor, appointment date, or start time.");
    }

    private async Task<TimeOnly> ValidateAndGetEndTimeAsync(
        Doctor doctor,
        DateOnly date,
        TimeOnly startTime,
        Guid? exceptAppointmentId,
        CancellationToken cancellationToken)
    {
        var schedules = await appointments.GetWorkingHoursAsync(doctor.Id, date.DayOfWeek, cancellationToken);
        var matchingSchedules = schedules.Where(item => item.IsActive && startTime >= item.StartTime && startTime < item.EndTime).ToArray();
        if (matchingSchedules.Length == 0)
            throw new InvalidOperationException("The requested time is outside the doctor's configured working schedule.");
        if (matchingSchedules.Length > 1)
            throw new InvalidOperationException("The doctor's working schedule contains overlapping entries. Resolve the schedule before accepting bookings.");
        var schedule = matchingSchedules[0];

        var duration = TimeSpan.FromMinutes(doctor.AppointmentDurationMinutes);
        var elapsedTicks = (startTime - schedule.StartTime).Ticks;
        if (elapsedTicks % duration.Ticks != 0)
            throw new InvalidOperationException("The requested time does not align with the doctor's configured appointment schedule.");

        var endTime = startTime.Add(duration);
        if (endTime > schedule.EndTime)
            throw new InvalidOperationException("The appointment would extend beyond the doctor's working hours.");
        if (await appointments.IsHolidayAsync(doctor.Id, date, cancellationToken))
            throw new InvalidOperationException("The doctor is unavailable on the selected date.");
        if (await appointments.HasBreakOverlapAsync(doctor.Id, date, startTime, endTime, cancellationToken))
            throw new InvalidOperationException("The appointment overlaps a doctor's break.");
        if (await appointments.HasAppointmentConflictAsync(doctor.Id, date, startTime, endTime, exceptAppointmentId, cancellationToken))
            throw new InvalidOperationException("The requested appointment time is already reserved.");

        return endTime;
    }
}

public sealed record PendingAppointmentDto(
    Guid Id,
    string PatientName,
    string DoctorName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime);
