using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Voice;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Stands in for the live voice provider: it records each session it is asked to start, and gives the server's watch
/// a connection the test can push events into. When the watch asks to close, it reports <see cref="SecondsAtClose"/>.
/// </summary>
public sealed class FakeVoiceProvider : IProtonVoiceProvider
{
    private int _next;

    public ConcurrentQueue<ProtonVoiceStart> Starts { get; } = new();

    public ConcurrentDictionary<string, FakeVoiceLink> Links { get; } = new();

    /// <summary>When set, starting a session fails as the provider refusing it would.</summary>
    public bool Refuse { get; set; }

    public int SecondsAtClose { get; set; } = 42;

    public Task<ProtonVoiceStarted> StartAsync(ProtonVoiceStart start, CancellationToken cancellationToken)
    {
        if (Refuse)
        {
            throw new ServiceUnavailableException(ErrorCodes.ProtonVoiceUnavailable, "Voice isn't available right now. You can still type your question.");
        }

        Starts.Enqueue(start);
        var id = $"live_test_{Interlocked.Increment(ref _next)}";
        Links[id] = new FakeVoiceLink(this);
        return Task.FromResult(new ProtonVoiceStarted(id, "v=0\r\no=- answer\r\n"));
    }

    public Task<IProtonVoiceLink> AttachAsync(string sessionId, CancellationToken cancellationToken)
        => Task.FromResult<IProtonVoiceLink>(Links[sessionId]);

    public void Reset()
    {
        Refuse = false;
        Starts.Clear();
    }
}

public sealed class FakeVoiceLink(FakeVoiceProvider provider) : IProtonVoiceLink
{
    private readonly Channel<JsonObject?> _events = Channel.CreateUnbounded<JsonObject?>();

    public ConcurrentQueue<JsonObject> Sent { get; } = new();

    public bool Disposed { get; private set; }

    /// <summary>An event from the provider, as the watch would receive it.</summary>
    public void Push(JsonObject message) => _events.Writer.TryWrite(message);

    public Task SendAsync(JsonObject command, CancellationToken cancellationToken)
    {
        Sent.Enqueue(command);
        if (command["type"]?.GetValue<string>() == "session.close")
        {
            Push(new JsonObject
            {
                ["type"] = "session.closed",
                ["reason"] = "close_requested",
                ["usage"] = new JsonObject { ["seconds"] = provider.SecondsAtClose },
            });
        }

        return Task.CompletedTask;
    }

    public async Task<JsonObject?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _events.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
