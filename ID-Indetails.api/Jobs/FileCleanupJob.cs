using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>Deletes uploads attached to nothing within a day, at start-up and then every few hours.</summary>
public sealed class FileCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<FileStorageOptions> options,
    ILogger<FileCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

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
                var deleted = await scope.ServiceProvider.GetRequiredService<IFileService>().DeleteUnusedAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} unused uploads.", deleted);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deleting unused uploads failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
