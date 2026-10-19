using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Repositories.Website.Abstraction;
using ID_Indetails.api.Services.Geocoding.Abstraction;

namespace ID_Indetails.api.Services.Website.Implementation;

/// <summary>Where a website project or office is now, before the change: null for a new one.</summary>
public sealed record StoredPlace(string? City, string? Country, decimal? Latitude, decimal? Longitude, bool Approximate);

/// <summary>
/// The place to keep: the names as the lists spell them. <paramref name="Problem"/> says what is wrong, for the person
/// to fix; the rest is what could be worked out.
/// </summary>
public sealed record ResolvedPlace(string? City, string? Country, decimal? Latitude, decimal? Longitude, bool Approximate, string? Problem);

/// <summary>
/// Works out a website project's or office's place (<see cref="WebsitePlaces"/>):
/// <list type="bullet">
/// <item>The country and city must be ones of the lists (<see cref="IPlaceDirectory"/>); the city is optional.</item>
/// <item>Coordinates typed in win; a missing country, and city when one is near, are found from them.</item>
/// <item>Without coordinates, the point is picked near the city, or near one of the country's towns when there is no
/// city: on land (a point near a coast can fall in the sea), never another project's or office's point, and kept
/// until the city or country changes.</item>
/// </list>
/// </summary>
public sealed class WebsitePlaceResolver(IPlaceDirectory directory, IGeocoder geocoder, IWebsiteRepository website)
{
    /// <summary>Each attempt asks the map service once, which answers at most once a second.</summary>
    private const int Attempts = 5;

    public async Task<ResolvedPlace> ResolveAsync(
        string? city, string? country, decimal? latitude, decimal? longitude, StoredPlace? stored, PlaceOwner owner,
        CancellationToken cancellationToken = default)
    {
        city = Clean(city);
        country = Clean(country);
        ResolvedPlace Problem(string message) => new(city, country, latitude, longitude, false, message);

        var land = country is null ? null : directory.Country(country);
        if (country is not null && land is null)
        {
            return Problem("Choose the country from the list.");
        }

        if (city is not null)
        {
            if (land is null)
            {
                return Problem("Choose the country first, then the city.");
            }

            var known = directory.City(land.Code, city);
            if (known is null)
            {
                return Problem($"Choose the city from {land.Name}'s list, or leave it empty.");
            }

            city = known.Name;
        }

        // the picked point sent back unchanged isn't typed in: it follows the city and country
        var echoed = stored is { Approximate: true } && latitude == stored.Latitude && longitude == stored.Longitude;
        if (latitude is { } lat && longitude is { } lng && !echoed)
        {
            if (land is null)
            {
                var town = directory.Nearest(lat, lng, WebsitePlaces.NearestCountryKilometres);
                land = town is null ? null : directory.Country(town.CountryCode);
                if (land is null)
                {
                    return Problem("We couldn't tell which country these coordinates are in: choose it from the list.");
                }
            }

            // the city is named only when one of the country's is close
            var near = directory.Nearest(lat, lng, WebsitePlaces.NearestCityKilometres);
            city ??= near?.CountryCode == land.Code ? near.Name : null;
            return new ResolvedPlace(city, land.Name, lat, lng, false, null);
        }

        if (land is null)
        {
            return new ResolvedPlace(null, null, null, null, false, null);
        }

        if (stored is { Approximate: true, Latitude: not null, Longitude: not null }
            && WebsitePlaces.SamePlace(stored.City, city) && WebsitePlaces.SamePlace(stored.Country, land.Name))
        {
            return new ResolvedPlace(city, land.Name, stored.Latitude, stored.Longitude, true, null);
        }

        DirectoryCity? Anchor() => city is not null ? directory.City(land.Code, city) : directory.RandomCity(land.Code, Random.Shared);
        var anchor = Anchor();
        if (anchor is null)
        {
            return Problem($"The list has no towns in {land.Name}: type the coordinates.");
        }

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var point = WebsitePlaces.RandomPointNear(anchor.Latitude, anchor.Longitude, Random.Shared);
            if (!await website.PositionTakenAsync(point.Latitude, point.Longitude, owner, cancellationToken)
                && await geocoder.CountryCodeAtAsync(point.Latitude, point.Longitude, cancellationToken) == land.Code)
            {
                return new ResolvedPlace(city, land.Name, point.Latitude, point.Longitude, true, null);
            }

            // a country alone: try near another of its towns
            anchor = city is null ? Anchor() ?? anchor : anchor;
        }

        // the town's own point is on land; when it's taken (or the map service is down), a point near it will do
        var own = (Latitude: Math.Round(anchor.Latitude, WebsitePlaces.Decimals), Longitude: Math.Round(anchor.Longitude, WebsitePlaces.Decimals));
        var fallback = await website.PositionTakenAsync(own.Latitude, own.Longitude, owner, cancellationToken)
            ? WebsitePlaces.RandomPointNear(anchor.Latitude, anchor.Longitude, Random.Shared)
            : own;
        return new ResolvedPlace(city, land.Name, fallback.Latitude, fallback.Longitude, true, null);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
