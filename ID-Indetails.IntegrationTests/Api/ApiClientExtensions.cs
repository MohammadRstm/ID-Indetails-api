using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Error;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Small helpers so the tests read like the flows they check.</summary>
public static class ApiClientExtensions
{
    public const string RefreshCookieName = "idi_refresh";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static Task<HttpResponseMessage> PostJsonAsync(
        this HttpClient client, string url, object body, string? accessToken = null, string? refreshCookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        return client.SendAsync(Authorize(request, accessToken, refreshCookie));
    }

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: Json) };
        return client.SendAsync(Authorize(request, accessToken, null));
    }

    public static Task<HttpResponseMessage> DeleteAsync(this HttpClient client, string url, string accessToken)
        => client.SendAsync(Authorize(new HttpRequestMessage(HttpMethod.Delete, url), accessToken, null));

    public static Task<HttpResponseMessage> PostEmptyAsync(
        this HttpClient client, string url, string? accessToken = null, string? refreshCookie = null)
        => client.SendAsync(Authorize(new HttpRequestMessage(HttpMethod.Post, url), accessToken, refreshCookie));

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string url, string accessToken)
        => client.SendAsync(Authorize(new HttpRequestMessage(HttpMethod.Get, url), accessToken, null));

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<ApiErrorResponse> ShouldFailWithAsync(this HttpResponseMessage response, int status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(status, body);

        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, Json)!;
        error.Code.ShouldBe(code, body);
        error.TraceId.ShouldNotBeNullOrWhiteSpace();
        return error;
    }

    public static async Task<AuthResponse> ShouldSignInAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue(body);
        return JsonSerializer.Deserialize<AuthResponse>(body, Json)!;
    }

    /// <summary>The refresh cookie value set by the response, or null.</summary>
    public static string? RefreshCookie(this HttpResponseMessage response)
        => SetCookieHeaders(response)
            .Where(h => h.StartsWith(RefreshCookieName + "=", StringComparison.Ordinal))
            .Select(h => h[(RefreshCookieName.Length + 1)..].Split(';')[0])
            .LastOrDefault();

    public static IEnumerable<string> SetCookieHeaders(this HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private static HttpRequestMessage Authorize(HttpRequestMessage request, string? accessToken, string? refreshCookie)
    {
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshCookie}");
        }

        return request;
    }
}
