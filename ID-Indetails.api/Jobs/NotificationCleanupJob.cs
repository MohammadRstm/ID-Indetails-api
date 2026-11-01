using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>Deletes notifications past a week (NotificationService.KeepFor), read or not, at start-up and then daily.</summary>
public sealed class NotificationCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<NotificationSchedulerOptions> options,
    ILogger<NotificationCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CleanupEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<INotificationService>().DeleteOldAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} notifications older than a week.", deleted);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deleting old notifications failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
