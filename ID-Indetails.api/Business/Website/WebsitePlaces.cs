namespace ID_Indetails.api.Business.Website;

/// <summary>
/// The website's places (docs/website.md). The country and city come from the lists; coordinates win; a city alone gets
/// a point near it, a country alone a point near one of its towns, chosen once and kept, never another one's point.
/// </summary>
public static class WebsitePlaces
{
    /// <summary>A picked point stays this close to its town in each direction (about 1 km).</summary>
    public const decimal Spread = 0.01m;

    /// <summary>Points are kept to six decimals (about 10 cm), as the database stores them.</summary>
    public const int Decimals = 6;

    /// <summary>How far from a typed point its city may be, to be named as its city.</summary>
    public const double NearestCityKilometres = 30;

    /// <summary>How far from a typed point a town may be, to tell its country.</summary>
    public const double NearestCountryKilometres = 150;

    /// <summary>A random point within <see cref="Spread"/> of a town, in each direction.</summary>
    public static (decimal Latitude, decimal Longitude) RandomPointNear(decimal latitude, decimal longitude, Random random)
    {
        var lat = latitude + (decimal)(random.NextDouble() * 2 - 1) * Spread;
        var lng = longitude + (decimal)(random.NextDouble() * 2 - 1) * Spread;
        return (Math.Round(Math.Clamp(lat, -90m, 90m), Decimals), Math.Round(Math.Clamp(lng, -180m, 180m), Decimals));
    }

    /// <summary>The same place, whatever the case and spaces.</summary>
    public static bool SamePlace(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
