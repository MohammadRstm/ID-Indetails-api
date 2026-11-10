using System.Text.Json;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>
/// Today's rates from the public feed, added like a super admin's but by no one (<see cref="ExchangeRate.CreatedById"/>
/// null, an <c>exchange_rate_set</c> event with no actor and <c>automatic: true</c>). Rates stay append-only: a currency
/// that already has a rate from today keeps it, and a feed rate too far from the last one is skipped.
/// </summary>
public sealed class ExchangeRateFeedService(
    IExchangeRateFeed feed,
    IExchangeRateRepository exchangeRates,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IOptions<ExchangeRateFeedOptions> options,
    IClock clock,
    ILogger<ExchangeRateFeedService> logger) : IExchangeRateFeedService
{
    public async Task<int> AddTodaysRatesAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.CompanyToday;
        var due = new List<(string Currency, ExchangeRate? Last)>();
        foreach (var currency in options.Value.Currencies.Select(c => c.Trim().ToUpperInvariant()).Distinct())
        {
            var last = await exchangeRates.GetInForceAsync(currency, today, cancellationToken);
            if (last is null || last.EffectiveFrom < today)
            {
                due.Add((currency, last));
            }
        }

        if (due.Count == 0)
        {
            return 0;
        }

        var quotes = await feed.RatesPerUsdAsync(cancellationToken);
        var added = 0;
        foreach (var (currency, last) in due)
        {
            if (!quotes.TryGetValue(currency, out var quoted))
            {
                logger.LogWarning("The exchange rate feed has no {Currency} rate.", currency);
                continue;
            }

            var rate = Math.Round(quoted, 6, MidpointRounding.AwayFromZero);
            if (!ExchangeRateRules.IsFeedRateUsable(rate, last?.RatePerUsd, options.Value.MaxChange))
            {
                logger.LogWarning(
                    "Skipped the feed's {Currency} rate of {Rate}: more than {MaxChange:P0} from the last rate, {Last}. The last rate stays in force.",
                    currency, rate, options.Value.MaxChange, last?.RatePerUsd);
                continue;
            }

            if (await AddAsync(currency, rate, today, cancellationToken))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>False when someone added a rate from today meanwhile: theirs stands.</summary>
    private async Task<bool> AddAsync(string currency, decimal ratePerUsd, DateOnly today, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (ExchangeRateRules.Check(today, today, await exchangeRates.GetLatestDateAsync(currency, cancellationToken)) is not null)
        {
            return false;
        }

        var rate = new ExchangeRate { Currency = currency, RatePerUsd = ratePerUsd, EffectiveFrom = today, CreatedAt = clock.UtcNow };
        exchangeRates.Add(rate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(new ActivityEvent
        {
            Kind = ActivityEventKinds.ExchangeRateSet,
            OccurredAt = clock.UtcNow,
            SubjectType = "exchange_rate",
            SubjectId = rate.Id,
            Amount = rate.RatePerUsd,
            Payload = JsonSerializer.Serialize(new { currency, effectiveFrom = today, automatic = true, source = options.Value.Source }),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
