using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Billing.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>
/// ExchangeRate-API's open endpoint (no key, updated once a day). Its terms ask for attribution where the rates are
/// shown, which the client's exchange rate settings carry.
/// </summary>
public sealed class OpenExchangeRateFeed(HttpClient http, IOptions<ExchangeRateFeedOptions> options) : IExchangeRateFeed
{
    private sealed record Reply(
        string? Result,
        [property: JsonPropertyName("base_code")] string? BaseCode,
        Dictionary<string, decimal>? Rates);

    public async Task<IReadOnlyDictionary<string, decimal>> RatesPerUsdAsync(CancellationToken cancellationToken = default)
    {
        var reply = await http.GetFromJsonAsync<Reply>(options.Value.Url, cancellationToken);
        if (reply is not { Result: "success", Rates: { } rates }
            || !string.Equals(reply.BaseCode, PaymentRules.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The exchange rate feed didn't answer with rates per USD.");
        }

        return new Dictionary<string, decimal>(rates, StringComparer.OrdinalIgnoreCase);
    }
}
