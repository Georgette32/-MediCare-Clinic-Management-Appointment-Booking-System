using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Maintenance;

/// <summary>
/// The clinic maintenance job (the class keeps its original name). Each run gets its own DI scope and performs
/// independent parts, each isolated so that a failure in one does not stop the others:
/// (a) clinic-unavailable processing for unresolved disruption appointments whose time has passed;
/// (b) grace-period start and No-Show processing for every active doctor on the clinic date;
/// (c) the stale-queue sweep for attendance attempts left over from earlier clinic days;
/// (d) previous-day Confirmed appointments: clinic unavailable (open worklist), No-Show, or patient left;
/// (e) day-before appointment reminders (idempotent per appointment).
/// Failures are logged and retried at the next interval.
/// </summary>
public sealed class DisruptionMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<DisruptionMaintenanceOptions> options,
    ILogger<DisruptionMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalMinutes));
        await RunOnceAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    /// <summary>Runs the parts in one scope. Public for tests of the isolation behaviour.</summary>
    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        await RunPartAsync("disruption processing", stoppingToken, async ct =>
        {
            var disruptions = provider.GetRequiredService<IDisruptionService>();
            var clock = provider.GetRequiredService<IClinicClock>();
            var cancelled = await disruptions.ProcessUnresolvedAsync(clock.UtcNow, ct);
            if (cancelled > 0)
                logger.LogInformation("Cancelled {Count} unresolved disruption appointment(s) as clinic unavailable.", cancelled);
        });

        await RunPartAsync("appointment length changes", stoppingToken, async ct =>
        {
            var tasks = provider.GetRequiredService<IClinicMaintenanceTasks>();
            var result = await tasks.PromoteDueDurationChangesAsync(ct);
            if (result.Promoted > 0)
                logger.LogInformation("Promoted {Count} scheduled appointment length change(s).", result.Promoted);
            foreach (var failure in result.Failures)
                logger.LogError("Appointment length change failed: {Failure}", failure);
        });

        await RunPartAsync("grace period processing", stoppingToken, async ct =>
        {
            var tasks = provider.GetRequiredService<IClinicMaintenanceTasks>();
            var result = await tasks.ProcessGracePeriodsForActiveDoctorsAsync(ct);
            if (result.Changes > 0)
                logger.LogInformation("Grace-period processing changed {Count} appointment(s).", result.Changes);
            foreach (var failure in result.Failures)
                logger.LogError("Grace-period processing failed: {Failure}", failure);
        });

        await RunPartAsync("stale queue sweep", stoppingToken, async ct =>
        {
            var tasks = provider.GetRequiredService<IClinicMaintenanceTasks>();
            var result = await tasks.SweepStaleQueueEntriesAsync(ct);
            if (result.Cancelled > 0 || result.Completed > 0)
                logger.LogInformation("Stale queue sweep cancelled {Cancelled} and completed {Completed} entr(ies).", result.Cancelled, result.Completed);
            foreach (var failure in result.Failures)
                logger.LogError("Stale queue sweep failed: {Failure}", failure);
        });

        // Runs after the stale sweep so that previous-day attempts are already closed when they are classified.
        await RunPartAsync("previous-day appointment resolution", stoppingToken, async ct =>
        {
            var resolver = provider.GetRequiredService<IPreviousDayAppointmentResolver>();
            var result = await resolver.ResolveAsync(ct);
            if (result.ClinicUnavailable + result.NoShow + result.PatientLeft > 0)
                logger.LogInformation(
                    "Previous-day appointments resolved: {ClinicUnavailable} clinic unavailable, {NoShow} no-show, {PatientLeft} patient left.",
                    result.ClinicUnavailable, result.NoShow, result.PatientLeft);
            foreach (var failure in result.Failures)
                logger.LogError("Previous-day appointment resolution failed: {Failure}", failure);
        });

        // Isolated: a reminder problem is logged and never affects the parts above or the next run.
        await RunPartAsync("appointment reminders", stoppingToken, async ct =>
        {
            var reminders = provider.GetRequiredService<IAppointmentReminderService>();
            var attempted = await reminders.SendDueRemindersAsync(ct);
            if (attempted > 0)
                logger.LogInformation("Checked {Count} appointment reminder(s) for tomorrow.", attempted);
        });
    }

    private async Task RunPartAsync(string partName, CancellationToken stoppingToken, Func<CancellationToken, Task> part)
    {
        try
        {
            await part(stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Clinic maintenance part '{Part}' failed. It will be retried at the next interval.", partName);
        }
    }
}
