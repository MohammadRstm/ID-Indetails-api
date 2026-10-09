using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>Every 15 minutes, checks the receipts of earlier pushes and deletes the devices Expo reports as no longer registered.</summary>
public sealed class PushReceiptsJob(
    IServiceScopeFactory scopes,
    IOptions<PushOptions> options,
    ILogger<PushReceiptsJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IPushDelivery>().CheckReceiptsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Checking push receipts failed.");
            }
        }
    }
}
