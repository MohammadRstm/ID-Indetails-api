using ID_Indetails.api.Business.Website;

namespace ID_Indetails.api.Services.Geocoding.Abstraction;

/// <summary>
/// Finds places on the map for the company website: the city and country at a point, and where a city is. Answers
/// null when the place can't be found or the map service doesn't answer; callers decide what that means.
/// </summary>
public interface IGeocoder
{
    Task<GeoPlace?> PlaceAtAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken = default);

    Task<GeoArea?> AreaOfAsync(string city, string country, CancellationToken cancellationToken = default);
}
