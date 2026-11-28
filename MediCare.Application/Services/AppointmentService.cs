using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Appointment workflow. Every public mutator runs through IUnitOfWork.ExecuteSerializableWithRetryAsync, so the
/// serializable check-and-write is retried on deadlock or unique-index races, and a final unique-index conflict is
/// reported as a user-facing refusal (for example "The requested appointment time is already reserved.").
/// </summary>
public sealed class AppointmentService(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IQueueRepository queue,
    IDoctorAvailabilityService availability,
    IPatientNotificationPublisher? patientNotifications = null,
    IStaffNotificationPublisher? staffNotifications = null)
{
    private readonly IPatientNotificationPublisher _notifications = patientNotifications ?? SilentPatientNotificationPublisher.Instance;
    private readonly IStaffNotificationPublisher _staff = staffNotifications ?? SilentStaffNotificationPublisher.Instance;

    public async Task<Guid> RequestAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly startTime, CancellationToken cancellationToken)
    {
        var (appointmentId, doctorName) = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var doctor = await GetBookingDoctorAsync(patientId, doctorId, ct);
            AppointmentPolicy.EnsureNotInPast(date, startTime, clinicClock.Today, clinicClock.CurrentTime);
            var endTime = await ValidateAndGetEndTimeAsync(doctor, date, startTime, exceptAppointmentId: null, ct);
            await EnsurePatientNotOverlappingAsync(patientId, date, startTime, endTime, Array.Empty<Guid>(), ct);
            var appointment = Appointment.Request(patientId, doctorId, date, startTime, endTime, clinicClock.UtcNow);
            await appointments.AddAsync(appointment, ct);
            return (appointment.Id, doctor.FullName);
        }, cancellationToken);

        // Reception confirms or rejects patient requests, so it hears about each one once it has committed.
        await _staff.PublishToReceptionAsync(
            StaffNotificationKind.AppointmentRequested,
            "New appointment request",
            $"A patient requested an appointment with {doctorName} on {date:yyyy-MM-dd} at {startTime:h\\:mm tt}. Confirm or reject it on the Requests page.",
            nameof(Appointment),
            appointmentId,
            cancellationToken);
        return appointmentId;
    }

    /// <summary>
    /// Reception/Admin booking for a patient. The clinic is the confirmation, so the request and its confirmation are
    /// written in one serializable transaction and the appointment ends Confirmed. Uses the same availability rules as
    /// patient booking, including approved working-hour extensions.
    /// </summary>
    public async Task<Guid> BookByClinicAsync(
        Guid patientId,
        Guid doctorId,
        DateOnly date,
        TimeOnly startTime,
        ClinicCancellationActor actor,
        CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);
        var booking = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var doctor = await GetBookingDoctorAsync(patientId, doctorId, ct);
            AppointmentPolicy.EnsureNotInPast(date, startTime, clinicClock.Today, clinicClock.CurrentTime);
            var endTime = await ValidateAndGetEndTimeAsync(doctor, date, startTime, exceptAppointmentId: null, ct);
            await EnsurePatientNotOverlappingAsync(patientId, date, startTime, endTime, Array.Empty<Guid>(), ct);
            var now = clinicClock.UtcNow;
            var appointment = Appointment.Request(patientId, doctorId, date, startTime, endTime, now);
            appointment.Confirm(now);
            await appointments.AddAsync(appointment, ct);
            var notification = new PatientNotification(
                patientId,
                PatientNotificationKind.AppointmentBookedByClinic,
                "Appointment booked",
                $"The clinic booked an appointment for you with {doctor.FullName} on {date:yyyy-MM-dd} at {startTime:h\\:mm tt}. It is confirmed.",
                appointment.Id);
            return (AppointmentId: appointment.Id, Notification: notification);
        }, cancellationToken);

        // Published only after the booking has committed. Delivery failures never fail the booking.
        await _notifications.PublishAsync(booking.Notification, cancellationToken);
        return booking.AppointmentId;
    }

    /// <summary>
    /// Patient cancellation. Cancelling the original also cancels its live Pending replacement in the same transaction,
    /// so no orphaned request can be confirmed later.
    /// </summary>
    public Task CancelByPatientAsync(Guid appointmentId, Guid patientId, string? reason, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableWithRetryAsync<bool>(async ct =>
        {
            var appointment = await GetAppointmentAsync(appointmentId, ct);
            if (appointment.PatientId != patientId)
                throw new UnauthorizedAccessException("Patients can cancel only their own appointments.");

            var now = clinicClock.UtcNow;
            appointment.CancelByPatient(clinicClock.Today, now, reason);
            await CancelLiveReplacementAsync(appointment.Id, now, ct);
            return true;
        }, cancellationToken);

    public async Task CancelByClinicAsync(Guid appointmentId, ClinicCancellationActor actor, string reason, CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));

        var notification = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var appointment = await GetAppointmentAsync(appointmentId, ct);
            EnsureNotBeforeClinicToday(appointment);
            await CancelByClinicCoreAsync(appointmentId, reason, ct);
            return new PatientNotification(
                appointment.PatientId,
                PatientNotificationKind.AppointmentCancelledByClinic,
                "Appointment cancelled",
                $"Your appointment on {appointment.AppointmentDate:yyyy-MM-dd} at {appointment.StartTime:h\\:mm tt} was cancelled by the clinic. Reason: {reason.Trim()}. Please contact the clinic to rebook.",
                appointment.Id);
        }, cancellationToken);

        await _notifications.PublishAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Clinic cancellation for callers that already own the transaction (the disruption worklist and the maintenance
    /// job). Performs no actor or day check and does not save or commit; the caller must do both. A live Pending
    /// replacement of the original is cancelled with it.
    /// </summary>
    public async Task CancelByClinicCoreAsync(Guid appointmentId, string reason, CancellationToken cancellationToken)
    {
        var appointment = await GetAppointmentAsync(appointmentId, cancellationToken);
        var now = clinicClock.UtcNow;
        await ReleaseWaitingQueueEntryAsync(appointmentId, cancellationToken);
        appointment.CancelByClinic(reason, now);
        await CancelLiveReplacementAsync(appointmentId, now, cancellationToken);
    }

    /// <summary>
    /// Patient reschedule request: creates a linked Pending replacement while the original keeps its slot. Refused once
    /// the original's day has arrived, and when the replacement would overlap another live appointment of the patient.
    /// </summary>
    public async Task<Guid> RescheduleAsync(Guid appointmentId, Guid patientId, Guid replacementDoctorId, DateOnly newDate, TimeOnly newStartTime, CancellationToken cancellationToken)
    {
        var (replacementId, doctorName, originalDate, originalStart) = await RequestRescheduleCoreAsync(
            appointmentId, patientId, replacementDoctorId, newDate, newStartTime, cancellationToken);

        await _staff.PublishToReceptionAsync(
            StaffNotificationKind.RescheduleRequested,
            "New reschedule request",
            $"A patient asked to move their {originalDate:yyyy-MM-dd} {originalStart:h\\:mm tt} appointment to {newDate:yyyy-MM-dd} at {newStartTime:h\\:mm tt} with {doctorName}. Confirm or reject it on the Requests page.",
            nameof(Appointment),
            replacementId,
            cancellationToken);
        return replacementId;
    }

    private Task<(Guid ReplacementId, string DoctorName, DateOnly OriginalDate, TimeOnly OriginalStart)> RequestRescheduleCoreAsync(
        Guid appointmentId, Guid patientId, Guid replacementDoctorId, DateOnly newDate, TimeOnly newStartTime, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var original = await GetAppointmentAsync(appointmentId, ct);
            if (original.PatientId != patientId)
                throw new UnauthorizedAccessException("Patients can reschedule only their own appointments.");
            if (original.AppointmentDate <= clinicClock.Today)
                throw new InvalidOperationException("Patients may request rescheduling only before the appointment day.");
            EnsureReplacementUsesDifferentSlot(original, replacementDoctorId, newDate, newStartTime);
            AppointmentPolicy.EnsureNotInPast(newDate, newStartTime, clinicClock.Today, clinicClock.CurrentTime);
            if (await appointments.HasLiveRescheduledSuccessorAsync(appointmentId, ct))
                throw new InvalidOperationException("This appointment already has a live replacement request.");

            var doctor = await GetDoctorAsync(replacementDoctorId, ct);
            var endTime = await ValidateAndGetEndTimeAsync(doctor, newDate, newStartTime, original.Id, ct);
            await EnsurePatientNotOverlappingAsync(original.PatientId, newDate, newStartTime, endTime, new[] { original.Id }, ct);
            var replacement = original.CreateRescheduledReplacement(replacementDoctorId, newDate, newStartTime, endTime, clinicClock.UtcNow);
            await appointments.AddAsync(replacement, ct);
            return (replacement.Id, doctor.FullName, original.AppointmentDate, original.StartTime);
        }, cancellationToken);

    public async Task<Guid> RescheduleByClinicAsync(
        Guid appointmentId,
        ClinicCancellationActor actor,
        Guid replacementDoctorId,
        DateOnly newDate,
        TimeOnly newStartTime,
        CancellationToken cancellationToken)
    {
        EnsureClinicActor(actor);
        var (replacementId, notification) = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            AppointmentPolicy.EnsureNotInPast(newDate, newStartTime, clinicClock.Today, clinicClock.CurrentTime);
            var patientId = (await GetAppointmentAsync(appointmentId, ct)).PatientId;
            var id = await RescheduleByClinicCoreAsync(appointmentId, replacementDoctorId, newDate, newStartTime, ct);
            return (id, new PatientNotification(
                patientId,
                PatientNotificationKind.AppointmentRescheduledByClinic,
                "Appointment moved",
                $"Your appointment has been moved to {newDate:yyyy-MM-dd} at {newStartTime:h\\:mm tt}.",
                id));
        }, cancellationToken);

        await _notifications.PublishAsync(notification, cancellationToken);
        return replacementId;
    }

    /// <summary>
    /// Clinic rescheduling for callers that already own the transaction (the disruption worklist). Performs no actor
    /// check and does not save or commit; the caller must do both. Refuses originals dated before the clinic today.
    /// </summary>
    public async Task<Guid> RescheduleByClinicCoreAsync(
        Guid appointmentId,
        Guid replacementDoctorId,
        DateOnly newDate,
        TimeOnly newStartTime,
        CancellationToken cancellationToken)
    {
        var original = await GetAppointmentAsync(appointmentId, cancellationToken);
        EnsureNotBeforeClinicToday(original);
        EnsureReplacementUsesDifferentSlot(original, replacementDoctorId, newDate, newStartTime);
        if (await appointments.HasLiveRescheduledSuccessorAsync(appointmentId, cancellationToken))
            throw new InvalidOperationException("This appointment already has a live replacement request.");

        var doctor = await GetDoctorAsync(replacementDoctorId, cancellationToken);
        var endTime = await ValidateAndGetEndTimeAsync(doctor, newDate, newStartTime, original.Id, cancellationToken);
        await EnsurePatientNotOverlappingAsync(original.PatientId, newDate, newStartTime, endTime, new[] { original.Id }, cancellationToken);
        await ReleaseWaitingQueueEntryAsync(original.Id, cancellationToken);
        var replacement = original.ReplaceByClinic(replacementDoctorId, newDate, newStartTime, endTime, clinicClock.UtcNow);
        await appointments.AddAsync(replacement, cancellationToken);
        return replacement.Id;
    }

    /// <summary>
    /// Confirms a Pending request only if it is still bookable: the same availability rules as booking are re-checked, and a
    /// replacement is refused once its original's day has arrived (Reception rejects it instead). The linked original is
    /// ignored by the overlap check.
    /// </summary>
    public async Task ConfirmAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var notification = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var appointment = await GetAppointmentAsync(appointmentId, ct);
            if (appointment.Status != AppointmentStatus.Pending)
                throw new InvalidOperationException("Only pending appointments can be confirmed or rejected.");

            Appointment? original = null;
            if (appointment.RescheduledFromAppointmentId is Guid originalAppointmentId)
            {
                original = await GetAppointmentAsync(originalAppointmentId, ct);
                if (original.AppointmentDate <= clinicClock.Today)
                    throw new InvalidOperationException("The original appointment's day has arrived; reject this reschedule request.");
            }

            var doctor = await GetDoctorAsync(appointment.DoctorId, ct);
            // Only the appointment itself is excluded: a replacement never overlaps its original (same slot is rejected at request time).
            var excluded = original is null ? new[] { appointment.Id } : new[] { appointment.Id, original.Id };
            await ValidateAndGetEndTimeAsync(doctor, appointment.AppointmentDate, appointment.StartTime, appointment.Id, ct);
            await EnsurePatientNotOverlappingAsync(appointment.PatientId, appointment.AppointmentDate, appointment.StartTime, appointment.EndTime, excluded, ct);

            var now = clinicClock.UtcNow;
            appointment.Confirm(now);
            original?.MarkRescheduled(now);

            var when = $"{appointment.AppointmentDate:yyyy-MM-dd} at {appointment.StartTime:h\\:mm tt}";
            return original is null
                ? new PatientNotification(appointment.PatientId, PatientNotificationKind.BookingRequestConfirmed,
                    "Booking confirmed", $"Your appointment on {when} is confirmed.", appointment.Id)
                : new PatientNotification(appointment.PatientId, PatientNotificationKind.RescheduleRequestConfirmed,
                    "Reschedule confirmed", $"Your appointment has been moved to {when}.", appointment.Id);
        }, cancellationToken);

        await _notifications.PublishAsync(notification, cancellationToken);
    }

    public async Task RejectAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var notification = await unitOfWork.ExecuteSerializableWithRetryAsync(async ct =>
        {
            var appointment = await GetAppointmentAsync(appointmentId, ct);
            appointment.Reject(clinicClock.UtcNow);

            var when = $"{appointment.AppointmentDate:yyyy-MM-dd} at {appointment.StartTime:h\\:mm tt}";
            return appointment.RescheduledFromAppointmentId is null
                ? new PatientNotification(appointment.PatientId, PatientNotificationKind.BookingRequestRejected,
                    "Booking not approved", $"Your booking request for {when} could not be approved. Please choose another time.", appointment.Id)
                : new PatientNotification(appointment.PatientId, PatientNotificationKind.RescheduleRequestRejected,
                    "Reschedule not approved", $"Your request to move your appointment to {when} could not be approved. Your original appointment stays as booked.", appointment.Id);
        }, cancellationToken);

        await _notifications.PublishAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingAppointmentDto>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var pendingAppointments = await appointments.GetPendingAsync(cancellationToken);
        return pendingAppointments.Select(item => new PendingAppointmentDto(
            item.Id,
            item.Patient.FullName,
            item.Doctor.FullName,
            item.AppointmentDate,
            item.StartTime,
            item.EndTime,
            item.Patient.PhoneNumber,
            item.RescheduledFromAppointment?.AppointmentDate,
            item.RescheduledFromAppointment?.StartTime)).ToArray();
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

    /// <summary>Clinic cancellation and rescheduling apply to today's and future appointments only.</summary>
    private void EnsureNotBeforeClinicToday(Appointment appointment)
    {
        if (appointment.AppointmentDate < clinicClock.Today)
            throw new InvalidOperationException("Clinic cancellation and rescheduling apply only to today's and future appointments.");
    }

    private static void EnsureReplacementUsesDifferentSlot(Appointment original, Guid replacementDoctorId, DateOnly newDate, TimeOnly newStartTime)
    {
        if (original.DoctorId == replacementDoctorId && original.AppointmentDate == newDate && original.StartTime == newStartTime)
            throw new InvalidOperationException("A replacement must use a different doctor, appointment date, or start time.");
    }

    /// <summary>
    /// A patient may hold only one live (Pending or Confirmed) appointment at a time, with any doctor. The excluded ids
    /// are the appointment itself and, for a replacement, its linked original.
    /// </summary>
    private async Task EnsurePatientNotOverlappingAsync(
        Guid patientId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        IReadOnlyCollection<Guid> excludedAppointmentIds,
        CancellationToken cancellationToken)
    {
        if (await appointments.HasLivePatientOverlapAsync(patientId, date, start, end, excludedAppointmentIds, cancellationToken))
            throw new InvalidOperationException("The patient already has an appointment at this time.");
    }

    /// <summary>Cancels the original's live Pending replacement, if any, in the caller's transaction.</summary>
    private async Task CancelLiveReplacementAsync(Guid originalAppointmentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var replacement = await appointments.GetLiveReplacementAsync(originalAppointmentId, cancellationToken);
        if (replacement is not null && replacement.Status == AppointmentStatus.Pending)
            replacement.CancelAsOriginalCancelled(now);
    }

    /// <summary>
    /// Cancelling or replacing an appointment removes its patient from the queue. A Waiting attempt is cancelled in the
    /// same transaction; a patient already Called or InConsultation cannot be cancelled this way.
    /// </summary>
    private async Task ReleaseWaitingQueueEntryAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var liveAttempt = await queue.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        if (liveAttempt is null)
            return;
        if (liveAttempt.Status is QueueEntryStatus.Called or QueueEntryStatus.InConsultation)
            throw new InvalidOperationException("The patient is already being served.");
        if (liveAttempt.Status == QueueEntryStatus.Waiting)
            liveAttempt.Cancel(clinicClock.UtcNow);
    }

    private async Task<TimeOnly> ValidateAndGetEndTimeAsync(
        Doctor doctor,
        DateOnly date,
        TimeOnly startTime,
        Guid? exceptAppointmentId,
        CancellationToken cancellationToken)
    {
        // A holiday removes every window for the date, so it is reported before window matching.
        if (await appointments.IsHolidayAsync(doctor.Id, date, cancellationToken))
            throw new InvalidOperationException("The doctor is unavailable on the selected date.");

        var windows = await availability.GetEffectiveWindowsAsync(doctor.Id, date, cancellationToken);
        var matchingWindows = windows.Where(item => startTime >= item.StartTime && startTime < item.EndTime).ToArray();
        if (matchingWindows.Length == 0)
            throw new InvalidOperationException("The requested time is outside the doctor's configured working schedule.");
        if (matchingWindows.Length > 1)
            throw new InvalidOperationException("The doctor's working schedule contains overlapping entries. Resolve the schedule before accepting bookings.");
        var window = matchingWindows[0];

        var duration = TimeSpan.FromMinutes(doctor.DurationOn(date));
        var elapsedTicks = (startTime - window.StartTime).Ticks;
        if (elapsedTicks % duration.Ticks != 0)
            throw new InvalidOperationException("The requested time does not align with the doctor's configured appointment schedule.");

        // Compared as TimeSpan: TimeOnly.Add wraps past midnight, so 23:40 + 20 min would become 00:00 and slip under the window end.
        if (startTime.ToTimeSpan() + duration > window.EndTime.ToTimeSpan())
            throw new InvalidOperationException("The appointment would extend beyond the doctor's working hours.");
        var endTime = startTime.Add(duration);
        if (await appointments.HasBreakOverlapAsync(doctor.Id, date, startTime, endTime, cancellationToken))
            throw new InvalidOperationException("The appointment overlaps a doctor's break.");
        if (await appointments.HasAppointmentConflictAsync(doctor.Id, date, startTime, endTime, exceptAppointmentId, cancellationToken))
            throw new InvalidOperationException("The requested appointment time is already reserved.");

        return endTime;
    }
}

/// <summary>Used when no publisher is supplied (tests and callers that do not notify). Never delivers anything.</summary>
internal sealed class SilentPatientNotificationPublisher : IPatientNotificationPublisher
{
    public static readonly SilentPatientNotificationPublisher Instance = new();

    public Task PublishAsync(PatientNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record PendingAppointmentDto(
    Guid Id,
    string PatientName,
    string DoctorName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string PatientPhone = "",
    DateOnly? OriginalDate = null,
    TimeOnly? OriginalStartTime = null);
