namespace ID_Indetails.api.Services.Geocoding.Abstraction;

/// <summary>
/// Asks a map service which country a point is in, to keep a point the website picks on land: one near a coast can
/// fall in the sea. Answers null when the point is in no country (the sea) or the service doesn't answer.
/// </summary>
public interface IGeocoder
{
    /// <summary>The ISO 3166-1 code of the country at the point, in upper case ("LB").</summary>
    Task<string?> CountryCodeAtAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken = default);
}
