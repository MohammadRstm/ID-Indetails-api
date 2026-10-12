using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// Sends through Expo's push service, which forwards to Apple and Google. Up to 100 messages a request, as Expo allows.
/// The optional access token ("enhanced push security") is a server secret, <c>Push:Expo:AccessToken</c>. Push tokens
/// and texts are never logged.
/// </summary>
public sealed class ExpoPushProvider(HttpClient http, IOptions<PushOptions> options, ILogger<ExpoPushProvider> logger) : IPushProvider
{
    public const string SendUrl = "https://exp.host/--/api/v2/push/send";
    public const string ReceiptsUrl = "https://exp.host/--/api/v2/push/getReceipts";
    public const int SendBatchSize = 100;
    public const int ReceiptBatchSize = 1000;

    private const string RequestFailed = "RequestFailed";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        var tickets = new List<PushTicket>(messages.Count);
        foreach (var batch in messages.Chunk(SendBatchSize))
        {
            tickets.AddRange(await SendBatchAsync(batch, cancellationToken));
        }

        return tickets;
    }

    public async Task<IReadOnlyDictionary<string, string>> ReceiptErrorsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string>();
        foreach (var batch in ticketIds.Chunk(ReceiptBatchSize))
        {
            using var response = await http.SendAsync(Request(ReceiptsUrl, new { ids = batch }), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Expo refused a receipts check with {Status}.", (int)response.StatusCode);
                continue;
            }

            var body = await response.Content.ReadFromJsonAsync<ExpoReceipts>(Json, cancellationToken);
            foreach (var (id, receipt) in body?.Data ?? [])
            {
                if (receipt.Status == "error")
                {
                    errors[id] = receipt.Details?.Error ?? receipt.Status;
                }
            }
        }

        return errors;
    }

    private async Task<IReadOnlyList<PushTicket>> SendBatchAsync(PushMessage[] batch, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(Request(SendUrl, batch.Select(ToExpo)), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Expo refused {Count} pushes with {Status}.", batch.Length, (int)response.StatusCode);
            return batch.Select(_ => new PushTicket(null, RequestFailed)).ToList();
        }

        var body = await response.Content.ReadFromJsonAsync<ExpoTickets>(Json, cancellationToken);
        var data = body?.Data ?? [];
        return batch.Select((_, i) => i < data.Count
                ? data[i].Status == "ok" ? new PushTicket(data[i].Id, null) : new PushTicket(null, data[i].Details?.Error ?? data[i].Status)
                : new PushTicket(null, RequestFailed))
            .ToList();
    }

    private HttpRequestMessage Request(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (options.Value.Expo.AccessToken is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static ExpoMessage ToExpo(PushMessage m) => new(
        m.To,
        m.Title,
        m.Body,
        m.Data,
        m.Sound ? "default" : null,
        m.Badge,
        m.ChannelId,
        m.Quiet ? "normal" : "high",
        m.Quiet ? "passive" : null);

    private sealed record ExpoMessage(
        string To,
        string? Title,
        string? Body,
        PushData Data,
        string? Sound,
        int Badge,
        string ChannelId,
        string Priority,
        string? InterruptionLevel);

    private sealed record ExpoDetails(string? Error);

    private sealed record ExpoTicket(string Status, string? Id, ExpoDetails? Details);

    private sealed record ExpoTickets(List<ExpoTicket>? Data);

    private sealed record ExpoReceipt(string Status, ExpoDetails? Details);

    private sealed record ExpoReceipts(Dictionary<string, ExpoReceipt>? Data);
}
