using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>
/// Client portal content upkeep: every hour, removes abandoned uploads and deletes the objects of files no longer
/// used; once a week, checks the content store against the rows. The first run is an hour after start-up.
/// </summary>
public sealed class PortalContentCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<PortalContentOptions> options,
    ILogger<PortalContentCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.JobsEnabled)
        {
            return;
        }

        var nextCheck = DateTime.UtcNow;
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IPortalContentMaintenance>();
                var handled = await maintenance.CleanUpAsync(stoppingToken);
                if (handled > 0)
                {
                    logger.LogInformation("Client portal cleanup handled {Count} uploads and files.", handled);
                }

                if (DateTime.UtcNow >= nextCheck)
                {
                    await maintenance.ReconcileAsync(stoppingToken);
                    nextCheck = DateTime.UtcNow + CheckEvery;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Client portal cleanup failed.");
            }
        }
    }
}
