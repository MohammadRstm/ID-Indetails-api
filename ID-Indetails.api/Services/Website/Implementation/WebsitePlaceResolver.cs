using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Repositories.Website.Abstraction;
using ID_Indetails.api.Services.Geocoding.Abstraction;

namespace ID_Indetails.api.Services.Website.Implementation;

/// <summary>Where a website project or office is now, before the change: null for a new one.</summary>
public sealed record StoredPlace(string? City, string? Country, decimal? Latitude, decimal? Longitude, bool Approximate);

/// <summary>
/// The place to keep. <paramref name="Problem"/> says what couldn't be found, for the person to type in; the rest is
/// what could be.
/// </summary>
public sealed record ResolvedPlace(string? City, string? Country, decimal? Latitude, decimal? Longitude, bool Approximate, string? Problem);

/// <summary>
/// Works out a website project's or office's place (<see cref="WebsitePlaces"/>):
/// <list type="bullet">
/// <item>Coordinates typed in win; a missing city or country is found from them.</item>
/// <item>A city and country alone get a random point in that city, on land (a point near a coast can fall in the
/// sea), which no other project or office has, and which is kept until the city or country changes.</item>
/// </list>
/// Lookups happen before anything is saved, outside any transaction.
/// </summary>
public sealed class WebsitePlaceResolver(IGeocoder geocoder, IWebsiteRepository website)
{
    /// <summary>Each attempt asks the map service once, which answers at most once a second.</summary>
    private const int Attempts = 5;

    public async Task<ResolvedPlace> ResolveAsync(
        string? city, string? country, decimal? latitude, decimal? longitude, StoredPlace? stored, PlaceOwner owner,
        CancellationToken cancellationToken = default)
    {
        city = Clean(city);
        country = Clean(country);

        // the random point sent back unchanged isn't typed in: it follows the city
        var echoed = stored is { Approximate: true } && latitude == stored.Latitude && longitude == stored.Longitude;
        if (latitude is { } lat && longitude is { } lng && !echoed)
        {
            if (city is not null && country is not null)
            {
                return new ResolvedPlace(city, country, lat, lng, false, null);
            }

            var found = await geocoder.PlaceAtAsync(lat, lng, cancellationToken);
            city ??= found?.City;
            country ??= found?.Country;
            return new ResolvedPlace(city, country, lat, lng, false,
                city is null || country is null ? "We couldn't find the city and country of these coordinates: type them in." : null);
        }

        if (city is null || country is null)
        {
            return new ResolvedPlace(city, country, null, null, false, null);
        }

        if (stored is { Approximate: true, Latitude: not null, Longitude: not null }
            && WebsitePlaces.SamePlace(stored.City, city) && WebsitePlaces.SamePlace(stored.Country, country))
        {
            return new ResolvedPlace(city, country, stored.Latitude, stored.Longitude, true, null);
        }

        var area = await geocoder.AreaOfAsync(city, country, cancellationToken);
        if (area is null)
        {
            return new ResolvedPlace(city, country, null, null, false,
                $"We couldn't find {city}, {country} on the map: check the spelling, or type the coordinates.");
        }

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var point = WebsitePlaces.RandomPointIn(area, Random.Shared);
            if (!await website.PositionTakenAsync(point.Latitude, point.Longitude, owner, cancellationToken)
                && await OnLandInAsync(point, country, cancellationToken))
            {
                return new ResolvedPlace(city, country, point.Latitude, point.Longitude, true, null);
            }
        }

        // the city's own point is on land; failing that, the map service is down and any point near it will do
        var centre = (Latitude: Math.Round(area.Latitude, WebsitePlaces.Decimals), Longitude: Math.Round(area.Longitude, WebsitePlaces.Decimals));
        var fallback = await website.PositionTakenAsync(centre.Latitude, centre.Longitude, owner, cancellationToken) ? WebsitePlaces.RandomPointIn(area, Random.Shared) : centre;
        return new ResolvedPlace(city, country, fallback.Latitude, fallback.Longitude, true, null);
    }

    /// <summary>A random point near a coast can fall in the sea: the map then names no country there, or another one.</summary>
    private async Task<bool> OnLandInAsync((decimal Latitude, decimal Longitude) point, string country, CancellationToken cancellationToken)
        => WebsitePlaces.SamePlace((await geocoder.PlaceAtAsync(point.Latitude, point.Longitude, cancellationToken))?.Country, country);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
