using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>
/// Adds today's exchange rates from the public feed once a day, from <see cref="ExchangeRateFeedOptions.FromHour"/>
/// (company time). Checked on a timer: a failed call (logged) is retried on the next tick, and once a day's call has
/// answered it isn't made again that day.
/// </summary>
public sealed class ExchangeRateFeedJob(
    IServiceScopeFactory scopes,
    IOptions<ExchangeRateFeedOptions> options,
    IClock clock,
    ILogger<ExchangeRateFeedJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        DateOnly? doneOn = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalMinutes));
        do
        {
            var today = clock.CompanyToday;
            if (doneOn == today || clock.ToCompanyTime(clock.UtcNow).Hour < options.Value.FromHour)
            {
                continue;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var added = await scope.ServiceProvider.GetRequiredService<IExchangeRateFeedService>().AddTodaysRatesAsync(stoppingToken);
                if (added > 0)
                {
                    logger.LogInformation("Added {Count} exchange rates from the feed.", added);
                }

                doneOn = today;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Adding today's exchange rates from the feed failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
