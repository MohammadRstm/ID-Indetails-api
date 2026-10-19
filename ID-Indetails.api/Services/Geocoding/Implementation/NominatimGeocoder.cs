using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Geocoding.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Geocoding.Implementation;

/// <summary>
/// OpenStreetMap's Nominatim (https://operations.osmfoundation.org/policies/nominatim/): English names, a named caller,
/// and at most one request a second from this API. Any failure is logged and answers null.
/// </summary>
public sealed class NominatimGeocoder(HttpClient http, IOptions<GeocodingOptions> options, ILogger<NominatimGeocoder> logger) : IGeocoder
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    public async Task<GeoPlace?> PlaceAtAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);
        using var json = await GetAsync($"reverse?format=jsonv2&zoom=10&lat={lat}&lon={lon}", cancellationToken);
        if (json is null || !json.RootElement.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = address.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var place = WebsitePlaces.PlaceOf(parts);
        return place.City is null && place.Country is null ? null : place;
    }

    public async Task<GeoArea?> AreaOfAsync(string city, string country, CancellationToken cancellationToken = default)
    {
        using var json = await GetAsync(
            $"search?format=jsonv2&limit=1&city={Uri.EscapeDataString(city)}&country={Uri.EscapeDataString(country)}", cancellationToken);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var hit = json.RootElement[0];
        // boundingbox is [south, north, west, east], as strings
        if (!hit.TryGetProperty("boundingbox", out var box) || box.GetArrayLength() != 4
            || !Number(hit, "lat", out var latitude) || !Number(hit, "lon", out var longitude))
        {
            return null;
        }

        var edges = box.EnumerateArray().Select(e => decimal.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null).ToList();
        return edges.Any(e => e is null) ? null : new GeoArea(latitude, longitude, edges[0]!.Value, edges[1]!.Value, edges[2]!.Value, edges[3]!.Value);
    }

    private async Task<JsonDocument?> GetAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return null;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastCallUtc.AddSeconds(1) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            _lastCallUtc = DateTime.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.BaseUrl.TrimEnd('/')}/{pathAndQuery}&accept-language=en");
            request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The map service answered {Status} to a place lookup.", (int)response.StatusCode);
                return null;
            }

            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "A place lookup failed.");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool Number(JsonElement element, string name, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(name, out var raw) && raw.ValueKind == JsonValueKind.String
            && decimal.TryParse(raw.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
