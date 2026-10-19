using ID_Indetails.api.Services.Geocoding.Abstraction;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The map service for tests: a point in Lebanon's or France's rough box is in that country, anything else is the sea,
/// and <see cref="Down"/> makes every lookup fail as if the service didn't answer. Never the internet.
/// </summary>
public sealed class FakeGeocoder : IGeocoder
{
    public bool Down { get; set; }

    public int Lookups { get; private set; }

    public Task<string?> CountryCodeAtAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
    {
        Lookups++;
        string? code = Down ? null
            : latitude is >= 33.0m and <= 34.7m && longitude is >= 35.1m and <= 36.7m ? "LB"
            : latitude is >= 42.0m and <= 51.0m && longitude is >= -5.0m and <= 8.0m ? "FR"
            : null;
        return Task.FromResult(code);
    }
}
