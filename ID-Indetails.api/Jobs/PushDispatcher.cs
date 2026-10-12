using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Push.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>
/// Sends the pushes requests and scheduled checks queue, in batches, each in a scope of its own. A batch that fails is
/// logged and dropped: the notifications themselves are already saved, and the in-app list shows them.
/// </summary>
public sealed class PushDispatcher(
    PushChannel channel,
    IServiceScopeFactory scopes,
    IOptions<PushOptions> options,
    ILogger<PushDispatcher> logger) : BackgroundService
{
    public const int MaxBatch = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (await channel.Reader.WaitToReadAsync(stoppingToken))
        {
            var jobs = new List<PushJob>();
            while (jobs.Count < MaxBatch && channel.Reader.TryRead(out var job))
            {
                jobs.Add(job);
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IPushDelivery>().DeliverAsync(jobs, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Couldn't send {Count} pushes.", jobs.Count);
            }
        }
    }
}
