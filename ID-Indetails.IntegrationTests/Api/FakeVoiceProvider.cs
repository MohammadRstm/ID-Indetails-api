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
    private readonly Lock _lock = new();
    private int _next;
    private int _attaches;
    private int _failAttaches;

    public ConcurrentQueue<ProtonVoiceStart> Starts { get; } = new();

    public ConcurrentDictionary<string, FakeVoiceLink> Links { get; } = new();

    /// <summary>When set, starting a session fails as the provider refusing it would.</summary>
    public bool Refuse { get; set; }

    public int SecondsAtClose { get; set; } = 42;

    /// <summary>When set, the provider starts the session, then answers only once it opens (a slow answer).</summary>
    public TaskCompletionSource? StartGate { get; set; }

    /// <summary>When set, attaching waits for it to open, or for the attach's own time limit.</summary>
    public TaskCompletionSource? AttachGate { get; set; }

    /// <summary>How many tries to attach the watch has made.</summary>
    public int Attaches => Volatile.Read(ref _attaches);

    /// <summary>How many of the next tries to attach fail, as a dropped connection would.</summary>
    public void FailAttaches(int count)
    {
        lock (_lock)
        {
            _failAttaches = count;
        }
    }

    public async Task<ProtonVoiceStarted> StartAsync(ProtonVoiceStart start, CancellationToken cancellationToken)
    {
        if (Refuse)
        {
            throw new ServiceUnavailableException(ErrorCodes.ProtonVoiceUnavailable, "Voice isn't available right now. You can still type your question.");
        }

        var id = $"live_test_{Interlocked.Increment(ref _next)}";
        Links[id] = new FakeVoiceLink(this);
        Starts.Enqueue(start);
        if (StartGate is { } gate)
        {
            // never longer than a test may wait
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return new ProtonVoiceStarted(id, "v=0\r\no=- answer\r\n");
    }

    public async Task<IProtonVoiceLink> AttachAsync(string sessionId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attaches);
        if (AttachGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        lock (_lock)
        {
            if (_failAttaches > 0)
            {
                _failAttaches--;
                throw new System.Net.WebSockets.WebSocketException("The provider dropped the connection.");
            }
        }

        return Links[sessionId];
    }

    public void Reset()
    {
        Refuse = false;
        StartGate = null;
        AttachGate = null;
        FailAttaches(0);
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
