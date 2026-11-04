using MediCare.Application;

namespace MediCare.Web.Services;

public sealed class AppointmentRecoveryWorker(IServiceScopeFactory scopes, ILogger<AppointmentRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
                await service.ExpireOffersAsync(stoppingToken);
                await service.ExpireAllCallsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Appointment recovery cycle failed."); }
        }
    }
}
