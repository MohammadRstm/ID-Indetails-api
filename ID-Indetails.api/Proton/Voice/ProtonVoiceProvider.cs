using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Voice;

/// <summary>What a new voice session starts with: its instructions, the conversation so far, and the browser's offer.</summary>
/// <param name="History">Earlier questions and answers of the same conversation, oldest first.</param>
/// <param name="SdpOffer">The browser's WebRTC offer; the provider's answer goes back to it.</param>
public sealed record ProtonVoiceStart(long UserId, string Instructions, IReadOnlyList<(string Question, string Answer)> History, string SdpOffer);

public sealed record ProtonVoiceStarted(string SessionId, string SdpAnswer);

/// <summary>
/// The live voice provider, seen from the server: it starts a session for a browser, and lets the server attach to a
/// running one to watch its usage and close it. Audio and the browser's events never pass through the server.
/// </summary>
public interface IProtonVoiceProvider
{
    Task<ProtonVoiceStarted> StartAsync(ProtonVoiceStart start, CancellationToken cancellationToken);

    Task<IProtonVoiceLink> AttachAsync(string sessionId, CancellationToken cancellationToken);
}

/// <summary>The server's own connection to a running session: JSON events in, commands out.</summary>
public interface IProtonVoiceLink : IAsyncDisposable
{
    Task SendAsync(JsonObject command, CancellationToken cancellationToken);

    /// <summary>The next event, or null once the connection has closed.</summary>
    Task<JsonObject?> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// OpenAI's GPT-Live (<c>POST /v1/live/sessions</c> over WebRTC, and the <c>/attach</c> WebSocket for the server). The
/// session uses client delegation: GPT-Live asks for help and the browser hands the question to Proton, so the
/// provider is given no tools and no company data. Nothing is stored on the provider's side.
/// </summary>
public sealed class OpenAiVoiceProvider(IHttpClientFactory clients, IOptions<ProtonOptions> options, ILoggerFactory loggers) : IProtonVoiceProvider
{
    public const string ClientName = "proton-voice";

    /// <summary>The provider's limit on startup history; a few exchanges stay far below it.</summary>
    private const int MaxHistoryMessages = 128;

    private readonly ILogger _log = loggers.CreateLogger(Tools.ProtonToolbox.LogCategory);

    private ProtonVoiceOptions Voice => options.Value.Voice;

    public async Task<ProtonVoiceStarted> StartAsync(ProtonVoiceStart start, CancellationToken cancellationToken)
    {
        var input = new JsonArray();
        foreach (var (question, answer) in start.History.TakeLast(MaxHistoryMessages / 2))
        {
            input.Add(Message("user", "input_text", question));
            input.Add(Message("assistant", "output_text", answer));
        }

        var body = new JsonObject
        {
            ["session"] = new JsonObject
            {
                ["model"] = Voice.Model,
                ["instructions"] = start.Instructions,
                ["input"] = input,
                ["audio"] = new JsonObject { ["output"] = new JsonObject { ["voice"] = Voice.Voice } },
                ["delegation"] = new JsonObject { ["type"] = "client" },
                ["store"] = false,
            },
            ["transport"] = new JsonObject { ["type"] = "webrtc", ["sdp"] = start.SdpOffer },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri(), "live/sessions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        Authorize(request.Headers);
        request.Headers.Add("OpenAI-Safety-Identifier", SafetyId(start.UserId));

        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning("Proton voice session: the provider couldn't be reached ({Error}).", exception.GetType().Name);
            throw new ServiceUnavailableException(ErrorCodes.ProtonVoiceUnavailable, "Voice isn't available right now. You can still type your question.");
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            // the provider's error codes only: its message may echo the request
            var error = await ErrorOfAsync(response, cancellationToken);
            _log.LogWarning("Proton voice session refused by the provider: HTTP {Status}, type {ErrorType}, code {ErrorCode}, param {ErrorParam}.",
                (int)response.StatusCode, error?["type"]?.ToString(), error?["code"]?.ToString(), error?["param"]?.ToString());
            throw new ServiceUnavailableException(ErrorCodes.ProtonVoiceUnavailable, response.StatusCode == HttpStatusCode.TooManyRequests
                ? "Voice is busy right now. Please try again in a moment, or type your question."
                : "Voice isn't available right now. You can still type your question.");
        }

        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var id = result?["session"]?["id"]?.GetValue<string>();
        var sdp = result?["transport"]?["sdp"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sdp))
        {
            _log.LogWarning("Proton voice session started without an id or an answer.");
            throw new ServiceUnavailableException(ErrorCodes.ProtonVoiceUnavailable, "Voice isn't available right now. You can still type your question.");
        }

        return new ProtonVoiceStarted(id, sdp);
    }

    public async Task<IProtonVoiceLink> AttachAsync(string sessionId, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {Voice.ApiKey}");
        var https = new Uri(BaseUri(), $"live/sessions/{Uri.EscapeDataString(sessionId)}/attach");
        var wss = new UriBuilder(https) { Scheme = https.Scheme == Uri.UriSchemeHttp ? "ws" : "wss" }.Uri;
        try
        {
            await socket.ConnectAsync(wss, cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new WebSocketVoiceLink(socket);
    }

    /// <summary>The provider's <c>error</c> object, when its response has one.</summary>
    private static async Task<JsonNode?> ErrorOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["error"];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Uri BaseUri() => new(Voice.BaseUrl.EndsWith('/') ? Voice.BaseUrl : Voice.BaseUrl + "/");

    private void Authorize(HttpRequestHeaders headers) => headers.Authorization = new AuthenticationHeaderValue("Bearer", Voice.ApiKey);

    private static JsonObject Message(string role, string type, string text) => new()
    {
        ["type"] = "message",
        ["role"] = role,
        ["content"] = new JsonArray(new JsonObject { ["type"] = type, ["text"] = text }),
    };

    /// <summary>A stable, unreadable id per person for the provider's abuse checks: never the id itself.</summary>
    private string SafetyId(long userId)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Voice.ApiKey), Encoding.UTF8.GetBytes($"user:{userId}")))[..32];
}

internal sealed class WebSocketVoiceLink(ClientWebSocket socket) : IProtonVoiceLink
{
    private readonly SemaphoreSlim _sending = new(1, 1);

    public async Task SendAsync(JsonObject command, CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(command.ToJsonString()), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sending.Release();
        }
    }

    public async Task<JsonObject?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, received.Count);
            if (!received.EndOfMessage)
            {
                continue;
            }

            if (received.MessageType == WebSocketMessageType.Text)
            {
                try
                {
                    if (JsonNode.Parse(message.ToArray()) is JsonObject json)
                    {
                        return json;
                    }
                }
                catch (JsonException)
                {
                    // not an event: skip it
                }
            }

            message.SetLength(0);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                // already gone
            }
        }

        socket.Dispose();
        _sending.Dispose();
    }
}
