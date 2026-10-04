using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Calls the hosted API as a person would. Every request moves the simulated clock on a little, and any answer but
/// success stops the seed, so the data never holds a step the API refused.
/// </summary>
public sealed class ApiClient(HttpClient http, SimulatedClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public int Requests { get; private set; }

    public Task<JsonElement> GetAsync(string token, string path) => SendAsync(HttpMethod.Get, path, token, body: null);

    public Task<JsonElement> PostAsync(string? token, string path, object? body = null, string? setupKey = null)
        => SendAsync(HttpMethod.Post, path, token, body, setupKey);

    public Task<JsonElement> PutAsync(string token, string path, object body) => SendAsync(HttpMethod.Put, path, token, body);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, string? token, object? body, string? setupKey = null)
    {
        clock.Tick();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (setupKey is not null)
        {
            request.Headers.Add("X-Setup-Key", setupKey);
        }

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new SeedingException($"{method} {path} at {clock.UtcNow:yyyy-MM-dd HH:mm} UTC answered {(int)response.StatusCode}: {text}");
        }

        Requests++;
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}

public sealed class SeedingException(string message) : Exception(message);

public static class JsonElementExtensions
{
    public static long Id(this JsonElement element) => element.GetProperty("id").GetInt64();
}
