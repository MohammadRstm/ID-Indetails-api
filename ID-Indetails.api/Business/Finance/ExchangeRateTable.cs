using ID_Indetails.api.Business.Billing;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// The company's dated exchange rates. An amount is converted at the rate in force on the day it
/// happened; with no rate on or before that day it can't be converted.
/// </summary>
public sealed class ExchangeRateTable
{
    public sealed record Rate(string Currency, DateOnly EffectiveFrom, decimal RatePerUsd);

    private readonly Dictionary<string, List<Rate>> _byCurrency;

    public ExchangeRateTable(IEnumerable<Rate> rates)
        => _byCurrency = rates
            .GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.EffectiveFrom).ToList(), StringComparer.OrdinalIgnoreCase);

    /// <returns>The USD amount, or null when no rate was in force that day.</returns>
    public decimal? ToUsd(decimal amount, string currency, DateOnly day)
    {
        if (string.Equals(currency, PaymentRules.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return amount;
        }

        var rate = _byCurrency.TryGetValue(currency, out var rates) ? rates.LastOrDefault(r => r.EffectiveFrom <= day) : null;
        return rate is null ? null : PaymentRules.ToUsd(amount, rate.RatePerUsd);
    }
}
