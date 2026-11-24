using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>
/// Resolves Confirmed appointments whose day has passed (Phase 2 leftover, decision: stale queue entries). Every
/// appointment is handled in its own unit of work, and a failure is reported without stopping the others:
/// 1. on an open disruption worklist: cancelled as clinic unavailable through the disruption path;
/// 2. otherwise, no attendance attempt at all: No-Show;
/// 3. otherwise, attempts exist but none completed and none is still live: cancelled with reason patient left before consultation.
/// Appointments with a live or completed attempt are left for the stale-queue sweep and the next run. Idempotent.
/// </summary>
public interface IPreviousDayAppointmentResolver
{
    Task<PreviousDayResolutionResult> ResolveAsync(CancellationToken cancellationToken);
}

public sealed record PreviousDayResolutionResult(
    int ClinicUnavailable,
    int NoShow,
    int PatientLeft,
    int Skipped,
    IReadOnlyList<string> Failures);

public sealed class PreviousDayAppointmentResolver(
    IPreviousDayAppointmentRepository previousDay,
    IAppointmentRepository appointments,
    IDisruptionService disruptions,
    AppointmentService appointmentService,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock) : IPreviousDayAppointmentResolver
{
    public async Task<PreviousDayResolutionResult> ResolveAsync(CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var candidates = await previousDay.GetConfirmedBeforeDateAsync(today, cancellationToken);
        var clinicUnavailable = 0;
        var noShow = 0;
        var patientLeft = 0;
        var skipped = 0;
        var failures = new List<string>();

        foreach (var appointmentId in candidates)
        {
            try
            {
                // The worklist path runs in its own transaction and publishes its notification after the commit.
                if (await disruptions.CancelPreviousDayWorklistAppointmentAsync(appointmentId, cancellationToken))
                {
                    clinicUnavailable++;
                    continue;
                }

                var outcome = await unitOfWork.ExecuteSerializableWithRetryAsync(
                    ct => ResolveOffWorklistAsync(appointmentId, today, ct), cancellationToken);
                switch (outcome)
                {
                    case PreviousDayOutcome.NoShow:
                        noShow++;
                        break;
                    case PreviousDayOutcome.PatientLeft:
                        patientLeft++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"Appointment {appointmentId:D}: {exception.Message}");
            }
        }

        return new PreviousDayResolutionResult(clinicUnavailable, noShow, patientLeft, skipped, failures);
    }

    private async Task<PreviousDayOutcome> ResolveOffWorklistAsync(Guid appointmentId, DateOnly today, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(appointmentId, cancellationToken);
        if (appointment is null || appointment.Status != AppointmentStatus.Confirmed || appointment.AppointmentDate >= today)
            return PreviousDayOutcome.Skipped;

        var attempts = await previousDay.GetAttemptStatusesAsync(appointmentId, cancellationToken);
        var now = clinicClock.UtcNow;

        if (attempts.Count == 0)
        {
            appointment.MarkNoShow(now);
            return PreviousDayOutcome.NoShow;
        }

        // A live attempt belongs to the stale-queue sweep, and a completed one means the appointment should already be
        // Completed; neither is resolved here.
        if (attempts.Any(status => status.IsLive() || status == QueueEntryStatus.Completed))
            return PreviousDayOutcome.Skipped;

        await appointmentService.CancelByClinicCoreAsync(appointmentId, AppointmentCancellationReasons.PatientLeftBeforeConsultation, cancellationToken);
        return PreviousDayOutcome.PatientLeft;
    }

    private enum PreviousDayOutcome
    {
        Skipped,
        NoShow,
        PatientLeft
    }
}
