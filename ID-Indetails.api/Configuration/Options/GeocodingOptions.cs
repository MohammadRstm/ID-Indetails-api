using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>The map service the company website's places are looked up with: OpenStreetMap's Nominatim by default.</summary>
public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>Off, nothing is looked up: coordinates and places are kept as typed.</summary>
    public bool Enabled { get; init; } = true;

    [Required, Url]
    public string BaseUrl { get; init; } = "https://nominatim.openstreetmap.org";

    /// <summary>Nominatim's usage policy asks every caller to name itself, with a way to reach it.</summary>
    [Required]
    public string UserAgent { get; init; } = "ID-InDetails/1.0 (+https://id-indetails.com)";

    [Range(1, 30)]
    public int TimeoutSeconds { get; init; } = 5;
}
