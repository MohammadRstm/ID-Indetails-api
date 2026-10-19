namespace ID_Indetails.api.Services.Geocoding.Abstraction;

/// <summary>A country, as the lists show it: its ISO 3166-1 code ("LB") and English name ("Lebanon").</summary>
public sealed record DirectoryCountry(string Code, string Name);

/// <summary>A city, town or village of GeoNames' list, with its point and how many people live there.</summary>
public sealed record DirectoryCity(string Name, string CountryCode, decimal Latitude, decimal Longitude, int Population);

/// <summary>
/// The countries and cities the website's places are chosen from, so a place is never misspelt and always has a
/// point. Read from GeoNames' list of places of 1,000 people or more, bundled with the API (docs/website.md).
/// </summary>
public interface IPlaceDirectory
{
    /// <summary>Every country with at least one city in the list, by name.</summary>
    IReadOnlyList<DirectoryCountry> Countries { get; }

    /// <summary>The country of this English name or code, whatever the case.</summary>
    DirectoryCountry? Country(string nameOrCode);

    /// <summary>
    /// Up to <paramref name="take"/> of the country's cities, biggest first; with <paramref name="search"/>, those whose
    /// name starts with it first, then those containing it, accents and case ignored.
    /// </summary>
    IReadOnlyList<DirectoryCity> Cities(string countryCode, string? search, int take);

    /// <summary>The country's city of this name, accents and case ignored; the biggest when several share it.</summary>
    DirectoryCity? City(string countryCode, string name);

    /// <summary>The nearest city to a point, within <paramref name="maxKilometres"/>; null when none is that close.</summary>
    DirectoryCity? Nearest(decimal latitude, decimal longitude, double maxKilometres);

    /// <summary>A city of the country picked at random; null when the list has none.</summary>
    DirectoryCity? RandomCity(string countryCode, Random random);
}
