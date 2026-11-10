namespace ID_Indetails.api.Services.Billing.Abstraction;

/// <summary>A public source of today's exchange rates.</summary>
public interface IExchangeRateFeed
{
    /// <summary>Units of each currency one US dollar buys, by currency code. Throws when the source doesn't answer with rates.</summary>
    Task<IReadOnlyDictionary<string, decimal>> RatesPerUsdAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adds today's rates from the feed, as the daily job does.</summary>
public interface IExchangeRateFeedService
{
    /// <summary>
    /// For each configured currency with no rate in force from today: today's rate from the feed, unless it is further
    /// from the last rate than allowed. Calls the feed only when a currency needs a rate. Returns the rates added.
    /// </summary>
    Task<int> AddTodaysRatesAsync(CancellationToken cancellationToken = default);
}
