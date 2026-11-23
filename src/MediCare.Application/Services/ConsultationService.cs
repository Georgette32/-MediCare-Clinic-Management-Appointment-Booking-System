using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Moves a clinic queue entry through consultation start and completion. A
/// completed visit is also recorded as a completed appointment so the doctor's
/// medical record, prescription and document access rules stay consistent.
/// </summary>
public sealed class ConsultationService(
    IQueueRepository queues,
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock)
{
    public async Task StartAsync(
        Guid queueEntryId,
        Guid doctorUserId,
        CancellationToken cancellationToken)
    {
        var doctor = await GetDoctorAsync(doctorUserId, cancellationToken);
        var preview = await GetEntryAsync(queueEntryId, cancellationToken);
        EnsureOwnedBy(preview, doctor.Id);
        EnsureToday(preview);

        await unitOfWork.ExecuteDoctorQueueWithRetryAsync(
            doctor.Id,
            preview.QueueDate,
            async ct =>
            {
                var entry = await GetEntryAsync(queueEntryId, ct);
                EnsureOwnedBy(entry, doctor.Id);
                entry.StartConsultation(clinicClock.UtcNow);
                return true;
            },
            cancellationToken);
    }

    public async Task CompleteAsync(
        Guid queueEntryId,
        Guid doctorUserId,
        string? notes,
        CancellationToken cancellationToken)
    {
        var doctor = await GetDoctorAsync(doctorUserId, cancellationToken);
        var preview = await GetEntryAsync(queueEntryId, cancellationToken);
        EnsureOwnedBy(preview, doctor.Id);
        EnsureToday(preview);

        await unitOfWork.ExecuteDoctorQueueWithRetryAsync(
            doctor.Id,
            preview.QueueDate,
            async ct =>
            {
                var entry = await GetEntryAsync(queueEntryId, ct);
                EnsureOwnedBy(entry, doctor.Id);

                var now = clinicClock.UtcNow;
                if (entry.Status == QueueEntryStatus.Called)
                {
                    entry.StartConsultation(now);
                }

                var completedAt = entry.ConsultationStartedAt is DateTime startedAt && now <= startedAt
                    ? startedAt.AddTicks(1)
                    : now;

                entry.Complete(completedAt);

                if (entry.AppointmentId is Guid appointmentId)
                {
                    var appointment = await appointments.GetByIdAsync(appointmentId, ct)
                        ?? throw new InvalidOperationException("The linked appointment was not found.");

                    if (appointment.DoctorProfileId != doctor.Id)
                    {
                        throw new UnauthorizedAccessException(
                            "Doctors can complete only their own appointments.");
                    }

                    if (appointment.Status == AppointmentStatus.Confirmed)
                    {
                        appointment.Complete(notes);
                    }
                }
                else
                {
                    var visitStartedAt = entry.CalledAt ?? entry.ArrivedAt;
                    var (start, end) = GetWalkInTimeRange(
                        visitStartedAt, doctor.AppointmentDurationMinutes);
                    var appointment = Appointment.Request(
                        entry.PatientProfileId,
                        doctor.Id,
                        entry.QueueDate,
                        start,
                        end,
                        now);
                    appointment.Confirm(now);
                    appointment.Complete(notes);
                    await appointments.AddAsync(appointment, ct);
                }

                return true;
            },
            cancellationToken);
    }

    private async Task<DoctorProfile> GetDoctorAsync(
        Guid doctorUserId,
        CancellationToken cancellationToken) =>
        await appointments.GetDoctorProfileByUserIdAsync(doctorUserId, cancellationToken)
        ?? throw new UnauthorizedAccessException(
            "The authenticated account is not linked to an active doctor profile.");

    private async Task<QueueEntry> GetEntryAsync(
        Guid queueEntryId,
        CancellationToken cancellationToken) =>
        await queues.GetByIdAsync(queueEntryId, cancellationToken)
        ?? throw new InvalidOperationException("The queue entry was not found.");

    private static void EnsureOwnedBy(QueueEntry entry, Guid doctorProfileId)
    {
        if (entry.DoctorProfileId != doctorProfileId)
        {
            throw new UnauthorizedAccessException(
                "Doctors can manage only their own clinic queue.");
        }
    }

    private void EnsureToday(QueueEntry entry)
    {
        if (entry.QueueDate != clinicClock.Today)
        {
            throw new InvalidOperationException(
                "Consultations can be managed only for today's clinic queue.");
        }
    }

    private static (TimeOnly Start, TimeOnly End) GetWalkInTimeRange(
        DateTime startedAt,
        int durationMinutes)
    {
        var duration = TimeSpan.FromMinutes(Math.Max(1, durationMinutes));
        var latestStartTicks = TimeOnly.MaxValue.Ticks - TimeSpan.TicksPerMinute;
        var startTicks = Math.Min(TimeOnly.FromDateTime(startedAt).Ticks, latestStartTicks);
        var endTicks = Math.Min(TimeOnly.MaxValue.Ticks, startTicks + duration.Ticks);

        return (
            TimeOnly.FromTimeSpan(TimeSpan.FromTicks(startTicks)),
            TimeOnly.FromTimeSpan(TimeSpan.FromTicks(endTicks)));
    }
}
