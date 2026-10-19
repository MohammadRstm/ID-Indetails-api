namespace ID_Indetails.api.Business.Website;

/// <summary>A point's city and country, as the map service names them in English; either may be missing.</summary>
public sealed record GeoPlace(string? City, string? Country);

/// <summary>Where a city is: its point, and the rectangle around it.</summary>
public sealed record GeoArea(decimal Latitude, decimal Longitude, decimal South, decimal North, decimal West, decimal East);

/// <summary>
/// The website's places (docs/website.md): coordinates win; a city and country alone get a random point in that city,
/// chosen once and kept, and never one another project or office already has.
/// </summary>
public static class WebsitePlaces
{
    /// <summary>A random point stays this close to the city's centre in each direction (about 2 km).</summary>
    public const decimal MaxSpread = 0.02m;

    /// <summary>Points are kept to six decimals (about 10 cm), as the database stores them.</summary>
    public const int Decimals = 6;

    /// <summary>
    /// The address parts that name the place, most specific first: small places have a town or village but no city,
    /// and some points only have a district or region.
    /// </summary>
    public static readonly string[] CityParts = ["city", "town", "village", "municipality", "county", "state"];

    /// <summary>The city and country in a map service's address.</summary>
    public static GeoPlace PlaceOf(IReadOnlyDictionary<string, string> address)
    {
        var city = CityParts.Select(address.GetValueOrDefault).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var country = address.GetValueOrDefault("country");
        return new GeoPlace(city?.Trim(), string.IsNullOrWhiteSpace(country) ? null : country.Trim());
    }

    /// <summary>
    /// A random point in the inner part of the city's rectangle (half its size, so not on its edges, where the sea or
    /// the countryside often is), and never more than <see cref="MaxSpread"/> from its centre.
    /// </summary>
    public static (decimal Latitude, decimal Longitude) RandomPointIn(GeoArea area, Random random)
    {
        var latSpread = Math.Min(Math.Abs(area.North - area.South) / 4, MaxSpread);
        var lngSpread = Math.Min(Math.Abs(area.East - area.West) / 4, MaxSpread);
        var latitude = area.Latitude + (decimal)(random.NextDouble() * 2 - 1) * latSpread;
        var longitude = area.Longitude + (decimal)(random.NextDouble() * 2 - 1) * lngSpread;
        return (Math.Round(Math.Clamp(latitude, -90m, 90m), Decimals), Math.Round(Math.Clamp(longitude, -180m, 180m), Decimals));
    }

    /// <summary>The same place, whatever the case and spaces.</summary>
    public static bool SamePlace(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
