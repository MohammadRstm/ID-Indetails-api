using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>
/// Deletes push devices whose session has ended (signed out elsewhere, a password change, 30 idle days, ...), at start-up
/// and then daily. Nothing is sent to them meanwhile: pushes only go to live sessions.
/// </summary>
public sealed class DeviceCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<PushOptions> options,
    ILogger<DeviceCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DeviceCleanupEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<IDeviceService>().DeleteDeadAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} push devices of ended sessions.", deleted);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deleting the push devices of ended sessions failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
