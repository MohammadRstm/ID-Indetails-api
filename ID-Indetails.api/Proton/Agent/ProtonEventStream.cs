using System.Text.Json;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Error;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Middlewares;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Agent;

/// <summary>
/// A question on the streaming endpoint. Every check runs before the first byte, so a refusal keeps its own status
/// code through the usual exception handling. After that the answer goes out as Server-Sent Events: <c>status</c>
/// (a fixed phrase per tool), <c>delta</c> (the answer as it is written), then <c>done</c> with the same reply the JSON
/// endpoint gives, or <c>error</c> in the uniform error contract. A comment every 10 seconds keeps proxies from timing
/// the connection out. If the person stops it or leaves, nothing more is sent: the question is still recorded, and any
/// change it proposed is dropped.
/// </summary>
public sealed class ProtonStreamingService(
    ProtonChatAgent agent,
    IOptions<ProtonOptions> options,
    IOptions<JsonOptions> json,
    ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger(Tools.ProtonToolbox.LogCategory);

    public async Task StreamAsync(HttpContext http, long userId, AskProtonRequest request, CancellationToken cancellationToken)
    {
        await using var question = await agent.PrepareAsync(http.User, userId, request, cancellationToken);
        await using var stream = new ProtonEventStream(http.Response, json.Value.JsonSerializerOptions);
        await stream.StartAsync(cancellationToken);
        try
        {
            var reply = await agent.AnswerAsync(question, stream, options.Value.StreamQuestionTimeoutSeconds, cancellationToken);
            await stream.DoneAsync(reply, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // stopped or gone: there is no one left to tell
        }
        catch (Exception exception)
        {
            var error = ApiErrorMapping.Of(exception, _log, http);
            await stream.ErrorAsync(ApiErrorWriter.Build(http, error.Type, error.Code, error.Message, error.Errors), CancellationToken.None);
        }
    }
}

/// <summary>The streaming endpoint's response, one event at a time: answer events and the timer's pings never interleave.</summary>
internal sealed class ProtonEventStream(HttpResponse response, JsonSerializerOptions json) : IProtonListener, IAsyncDisposable
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _writing = new(1, 1);

    private Timer? _ping;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        // nginx passes each event on at once instead of buffering the response
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.Body.FlushAsync(cancellationToken);
        _ping = new Timer(_ => _ = PingAsync(), null, PingEvery, PingEvery);
    }

    public Task StatusAsync(string phrase, CancellationToken cancellationToken) => SendAsync("status", new { text = phrase }, cancellationToken);

    public Task DeltaAsync(int trip, string text, CancellationToken cancellationToken) => SendAsync("delta", new { trip, text }, cancellationToken);

    public Task DoneAsync(ProtonReplyResponse reply, CancellationToken cancellationToken) => SendAsync("done", reply, cancellationToken);

    public Task ErrorAsync(ApiErrorResponse error, CancellationToken cancellationToken) => SendAsync("error", error, cancellationToken);

    private Task SendAsync(string name, object data, CancellationToken cancellationToken)
        => WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, json)}\n\n", cancellationToken);

    private async Task PingAsync()
    {
        try
        {
            await WriteAsync(": ping\n\n", CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the connection is gone; the question notices on its own
        }
    }

    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken);
        try
        {
            await response.WriteAsync(text, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _writing.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ping is not null)
        {
            await _ping.DisposeAsync();
        }
    }
}
