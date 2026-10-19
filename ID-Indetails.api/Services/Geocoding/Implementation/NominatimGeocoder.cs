using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Geocoding.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Geocoding.Implementation;

/// <summary>
/// OpenStreetMap's Nominatim (https://operations.osmfoundation.org/policies/nominatim/), asked only which country a point
/// is in: a named caller, and at most one request a second from this API. Any failure is logged and answers null.
/// </summary>
public sealed class NominatimGeocoder(HttpClient http, IOptions<GeocodingOptions> options, ILogger<NominatimGeocoder> logger) : IGeocoder
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    public async Task<string?> CountryCodeAtAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);
        // zoom 3 asks only for the country, the cheapest answer
        using var json = await GetAsync($"reverse?format=jsonv2&zoom=3&lat={lat}&lon={lon}", cancellationToken);
        return json is not null
            && json.RootElement.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object
            && address.TryGetProperty("country_code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString()!.ToUpperInvariant()
            : null;
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

}
