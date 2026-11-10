using ID_Indetails.api.Services.Billing.Abstraction;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The public exchange rate source for the tests: the rates it quotes are set by the test and its calls counted. Never the internet.</summary>
public sealed class FakeExchangeRateFeed : IExchangeRateFeed
{
    /// <summary>Units of each currency one US dollar buys, as the source quotes them now.</summary>
    public Dictionary<string, decimal> Quotes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<string, decimal>> RatesPerUsdAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>(Quotes, StringComparer.OrdinalIgnoreCase));
    }
}
