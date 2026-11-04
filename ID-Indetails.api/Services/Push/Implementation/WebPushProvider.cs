using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// Sends Web Push: each push is encrypted to its browser and posted to the browser's push service (Google's, Apple's,
/// Mozilla's or Microsoft's), signed with the server's VAPID key, <c>Push:WebPush</c>. There are no receipts: the push
/// service answers at once. Addresses, keys and texts are never logged.
/// </summary>
public sealed class WebPushProvider(
    HttpClient http,
    IOptions<PushOptions> options,
    IOptions<FrontendOptions> frontend,
    IClock clock) : IPushProvider
{
    /// <summary>How long a push service keeps a push for a browser that's offline. Older news is in the bell anyway.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromDays(1);

    private const string RequestFailed = "RequestFailed";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        var tickets = new List<PushTicket>(messages.Count);
        foreach (var message in messages)
        {
            tickets.Add(await SendOneAsync(message, cancellationToken));
        }

        return tickets;
    }

    public Task<IReadOnlyDictionary<string, string>> ReceiptErrorsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    private async Task<PushTicket> SendOneAsync(PushMessage message, CancellationToken cancellationToken)
    {
        // checked again here, though registering already refused anything else
        if (message.WebPush is not { } subscription || !WebPushEndpoints.IsAllowed(subscription.Endpoint))
        {
            return new PushTicket(null, IPushProvider.DeviceNotRegistered);
        }

        var vapid = options.Value.WebPush;
        var endpoint = new Uri(subscription.Endpoint);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new WebPushPayload(message.Title, message.Body, message.Data, message.Badge, message.Quiet), Json);
        var body = WebPushCrypto.Encrypt(payload, Base64Url.DecodeFromChars(subscription.P256dh), Base64Url.DecodeFromChars(subscription.Auth));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            WebPushCrypto.VapidAuthorization(endpoint.GetLeftPart(UriPartial.Authority), frontend.Value.BaseUrl, clock.UtcNow, vapid.PublicKey, vapid.PrivateKey));
        request.Headers.Add("TTL", ((int)TimeToLive.TotalSeconds).ToString());
        request.Headers.Add("Urgency", message.Quiet ? "normal" : "high");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => new PushTicket(null, null),
                // the browser unsubscribed, or the subscription expired
                HttpStatusCode.NotFound or HttpStatusCode.Gone => new PushTicket(null, IPushProvider.DeviceNotRegistered),
                var status => new PushTicket(null, $"Http{(int)status}"),
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // one push service down or slow doesn't hold up the others
            return new PushTicket(null, RequestFailed);
        }
    }

    /// <summary>What the app's service worker reads to show the notification (<c>src/sw.ts</c> in the client).</summary>
    /// <param name="Quiet">During quiet hours: shown without sound or vibration where the browser allows it.</param>
    private sealed record WebPushPayload(string? Title, string? Body, PushData Data, int Badge, bool Quiet);
}
