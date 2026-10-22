namespace ID_Indetails.api.Proton.Voice;

/// <summary>
/// A running voice session, as the server knows it: whose it is, which Proton conversation it speaks into, and which
/// of its spoken questions have been asked, so none is asked twice.
/// </summary>
public sealed class ProtonVoiceSession(long userId, Guid conversationId, DateTime startedAt, DateTime endsAt)
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();

    /// <summary>The provider's id, once it has started the session; empty while the place is only reserved.</summary>
    public string SessionId { get; private set; } = string.Empty;

    public long UserId => userId;

    public Guid ConversationId => conversationId;

    public DateTime StartedAt => startedAt;

    /// <summary>When the server closes it, whatever the browser does.</summary>
    public DateTime EndsAt => endsAt;

    /// <summary>Cancelled once the session should close: the person ended it, the budget ran out, or the app is stopping.</summary>
    public CancellationToken Closing => _closing.Token;

    /// <summary>Takes a spoken question by the provider's id for it; false if it was already taken.</summary>
    public bool Claim(string delegationId)
    {
        lock (_lock)
        {
            return _asked.Add(delegationId);
        }
    }

    public void Started(string sessionId) => SessionId = sessionId;

    public void Close()
    {
        try
        {
            _closing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already finished
        }
    }
}

/// <summary>The voice sessions running on this server, one per person at most. Lost on restart, like conversations.</summary>
public sealed class ProtonVoiceSessions
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, ProtonVoiceSession> _byUser = [];

    /// <summary>Holds the person's place before the provider is asked, so two tabs can't open two paid sessions.</summary>
    public bool TryReserve(ProtonVoiceSession session)
    {
        lock (_lock)
        {
            return _byUser.TryAdd(session.UserId, session);
        }
    }

    /// <summary>
    /// The person's running session with this id; null for anyone else's, or one that has ended or is closing (its
    /// place stays held until the provider has closed it, but it takes nothing more).
    /// </summary>
    public ProtonVoiceSession? Find(long userId, string sessionId)
    {
        lock (_lock)
        {
            return _byUser.TryGetValue(userId, out var session) && session.SessionId.Length > 0 && session.SessionId == sessionId
                   && !session.Closing.IsCancellationRequested
                ? session
                : null;
        }
    }

    public void Release(ProtonVoiceSession session)
    {
        lock (_lock)
        {
            if (_byUser.TryGetValue(session.UserId, out var held) && ReferenceEquals(held, session))
            {
                _byUser.Remove(session.UserId);
            }
        }
    }

    public IReadOnlyList<ProtonVoiceSession> All()
    {
        lock (_lock)
        {
            return [.. _byUser.Values];
        }
    }
}
