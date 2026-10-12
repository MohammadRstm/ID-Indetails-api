using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.History;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>
/// Deletes saved Proton chats nobody has used for the retention period (Proton:History:RetentionDays), with their
/// words in the usage ledger, at start-up and then once a day.
/// </summary>
public sealed class ProtonHistoryCleanupJob(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<ProtonOptions> options,
    ILogger<ProtonHistoryCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.History.CleanupEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var before = clock.UtcNow.AddDays(-options.Value.History.RetentionDays);
                var deleted = await scope.ServiceProvider.GetRequiredService<ProtonHistoryService>().DeleteUntouchedSinceAsync(before, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} Proton chats unused for {Days} days.", deleted, options.Value.History.RetentionDays);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deleting old Proton chats failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
