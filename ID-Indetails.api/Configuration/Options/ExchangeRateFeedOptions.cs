using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// The daily exchange rate feed (ExchangeRateFeedJob): each morning it adds today's rate of each currency from a public
/// source, unless one is already in force from today. Integration tests switch it off.
/// </summary>
public sealed class ExchangeRateFeedOptions
{
    public const string SectionName = "ExchangeRateFeed";

    public bool Enabled { get; init; } = true;

    /// <summary>A JSON document of rates per USD: <c>{ "result": "success", "base_code": "USD", "rates": { "LBP": 89500 } }</c>.</summary>
    [Required, Url]
    public string Url { get; init; } = "https://open.er-api.com/v6/latest/USD";

    /// <summary>Named in the activity event of each rate it adds.</summary>
    [Required]
    public string Source { get; init; } = "ExchangeRate-API";

    /// <summary>
    /// Set in appsettings.json (LBP): a default here would be added to, never replaced by, the configured list.
    /// </summary>
    [MinLength(1)]
    public string[] Currencies { get; init; } = [];

    /// <summary>The company hour from which it runs each day, so the source has published that day's rate.</summary>
    [Range(0, 23)]
    public int FromHour { get; init; } = 6;

    /// <summary>A rate further than this from the last one (0.10 is 10%) isn't added: the last one stays in force.</summary>
    [Range(typeof(decimal), "0.01", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal MaxChange { get; init; } = 0.10m;

    [Range(5, 1440)]
    public int IntervalMinutes { get; init; } = 60;

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 20;
}
