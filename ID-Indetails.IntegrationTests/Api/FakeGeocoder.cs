using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Services.Geocoding.Abstraction;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The map service for tests: Beirut and Byblos are known places, anything else isn't found, and <see cref="Down"/>
/// makes every lookup fail as if the service didn't answer.
/// </summary>
public sealed class FakeGeocoder : IGeocoder
{
    public static readonly GeoArea Beirut = new(33.8938m, 35.5018m, 33.8600m, 33.9200m, 35.4600m, 35.5500m);
    public static readonly GeoArea Byblos = new(34.1230m, 35.6519m, 34.1100m, 34.1400m, 35.6300m, 35.6700m);

    public bool Down { get; set; }

    public int Lookups { get; private set; }

    public Task<GeoPlace?> PlaceAtAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
    {
        Lookups++;
        GeoPlace? place = Down ? null
            : Inside(Beirut, latitude, longitude) ? new GeoPlace("Beirut", "Lebanon")
            : Inside(Byblos, latitude, longitude) ? new GeoPlace("Byblos", "Lebanon")
            : null;
        return Task.FromResult(place);
    }

    public Task<GeoArea?> AreaOfAsync(string city, string country, CancellationToken cancellationToken = default)
    {
        Lookups++;
        GeoArea? area = Down || !country.Equals("Lebanon", StringComparison.OrdinalIgnoreCase) ? null
            : city.Equals("Beirut", StringComparison.OrdinalIgnoreCase) ? Beirut
            : city.Equals("Byblos", StringComparison.OrdinalIgnoreCase) ? Byblos
            : null;
        return Task.FromResult(area);
    }

    public static bool Inside(GeoArea area, decimal latitude, decimal longitude)
        => latitude >= area.South && latitude <= area.North && longitude >= area.West && longitude <= area.East;
}
