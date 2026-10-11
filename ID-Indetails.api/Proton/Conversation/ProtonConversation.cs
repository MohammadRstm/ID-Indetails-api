using Anthropic.Models.Messages;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Conversation;

/// <summary>
/// One chat with Proton, held in memory on the server for its owner only. The message list is exactly what was sent
/// to the model, append-only, so the model's earlier reasoning stays valid when replayed.
/// </summary>
public sealed class ProtonConversation(Guid id, long userId, string accessFingerprint)
{
    private int _busy;

    public Guid Id => id;

    public long UserId => userId;

    /// <summary>The user's role, permissions and domains when the chat began: a change ends the chat.</summary>
    public string AccessFingerprint => accessFingerprint;

    public List<MessageParam> Messages { get; } = [];

    public ConversationFocus Focus { get; } = new();

    public int Questions { get; set; }

    /// <summary>Input tokens of the last model request (cached or not): the cost of carrying this conversation on.</summary>
    public int LastInputTokens { get; set; }

    /// <summary>The records tools have returned in this conversation: the only ones Proton may offer as choices.</summary>
    public Dictionary<(ProtonRefType Type, long Id), ProtonRef> Known { get; } = [];

    /// <summary>The options of the last reply that offered any; a pick must be one of them.</summary>
    public IReadOnlyList<Ui.ProtonChoice> OfferedChoices { get; set; } = [];

    /// <summary>The quick replies offered with them; a click must be one of these.</summary>
    public IReadOnlyList<Ui.ProtonQuickReply> OfferedReplies { get; set; } = [];

    /// <summary>The last reply's suggested replies: a tap names one by its place, and the text is taken from here.</summary>
    public IReadOnlyList<string> OfferedSuggestions { get; set; } = [];

    // a confirmation can arrive while a question is being answered
    private readonly Lock _pendingLock = new();
    private readonly Dictionary<Guid, DateTime> _pending = [];

    /// <summary>A proposal is waiting for its Confirm (until the user decides or it expires).</summary>
    public bool HasPendingAction(DateTime now)
    {
        lock (_pendingLock)
        {
            return _pending.Values.Any(expires => expires > now);
        }
    }

    public void Proposed(Guid actionId, DateTime expiresAt)
    {
        lock (_pendingLock)
        {
            _pending[actionId] = expiresAt;
        }
    }

    /// <summary>The user confirmed or cancelled a proposal: it waits no more.</summary>
    public void Decided(Guid actionId)
    {
        lock (_pendingLock)
        {
            _pending.Remove(actionId);
        }
    }

    private const int TypedKept = 30;

    private readonly Queue<string> _typed = new();

    /// <summary>The questions the user typed (not picked or tapped), newest last: where addresses and recipients may come from.</summary>
    public IReadOnlyCollection<string> Typed => _typed;

    public void RememberTyped(string text)
    {
        _typed.Enqueue(text);
        while (_typed.Count > TypedKept)
        {
            _typed.Dequeue();
        }
    }

    private const int RecapExchanges = 3;
    private const int RecapTextLength = 400;

    private readonly Queue<(string Question, string Answer)> _exchanges = new();

    /// <summary>
    /// What the app tells the model about the chat before a rollover, until a question after it is answered. Written
    /// by the app from the last questions and answers; null when there was no rollover.
    /// </summary>
    public string? Recap { get; private set; }

    /// <summary>The last few questions and answers, shortened: what a recap or a new voice session starts from.</summary>
    public IReadOnlyList<(string Question, string Answer)> Exchanges => [.. _exchanges];

    /// <summary>The last few questions and answers, as they were shown: what a rollover's recap is made of.</summary>
    public void RememberExchange(string question, string answer)
    {
        Recap = null;
        _exchanges.Enqueue((ProtonText.Cut(question, RecapTextLength)!, ProtonText.Cut(answer, RecapTextLength)!));
        while (_exchanges.Count > RecapExchanges)
        {
            _exchanges.Dequeue();
        }
    }

    /// <summary>
    /// Starts the chat afresh under the same id when it is too long to carry on: the messages go, and the next question
    /// gets a recap instead. What the tools found (focus, known records, offered options) stays, and so do pending
    /// proposals and their notes, which belong to this id.
    /// </summary>
    public void RollOver()
    {
        Messages.Clear();
        Questions = 0;
        LastInputTokens = 0;
        Recap = _exchanges.Count == 0 ? null : "Earlier in this chat (a recap by the app; the earlier messages are no longer available): "
            + string.Join(" ", _exchanges.Select(e => $"The user asked: \"{e.Question}\" You answered: \"{e.Answer}\""));
    }

    private readonly Queue<string> _actionNotes = new();

    /// <summary>
    /// What became of the actions the user confirmed or cancelled since the last question, as the confirmation flow
    /// recorded it (never the model): told to the model with the next question, then dropped.
    /// </summary>
    public void NoteAction(string note)
    {
        lock (_actionNotes)
        {
            _actionNotes.Enqueue(note);
            while (_actionNotes.Count > 5)
            {
                _actionNotes.Dequeue();
            }
        }
    }

    public IReadOnlyList<string> TakeActionNotes()
    {
        lock (_actionNotes)
        {
            var notes = _actionNotes.ToList();
            _actionNotes.Clear();
            return notes;
        }
    }

    /// <summary>One question at a time per conversation.</summary>
    public bool TryBegin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    public void End() => Interlocked.Exchange(ref _busy, 0);
}

/// <summary>
/// What the conversation is currently about, updated from the tools' results (not guessed from the text): the latest
/// project, client and people, and the latest period. Shown to the model with each question so "it", "they" and
/// "the month before" resolve the same way every time.
/// </summary>
public sealed class ConversationFocus
{
    public ProtonRef? Project { get; private set; }

    public ProtonRef? Client { get; private set; }

    public IReadOnlyList<ProtonRef> People { get; private set; } = [];

    public DayRange? Period { get; private set; }

    /// <summary>What the last analysis compared <see cref="Period"/> with.</summary>
    public DayRange? ComparedWith { get; private set; }

    /// <summary>What the last analysis looked at ("Hours compared", "Design money compared").</summary>
    public string? Analysis { get; private set; }

    public string? Topic { get; private set; }

    /// <summary>Takes in a successful tool call. A call about exactly one record of a kind makes that record the focus.</summary>
    public void Take(ProtonToolResult result)
    {
        if (result.Status != ProtonToolStatus.Ok)
        {
            return;
        }

        Topic = result.Tool;
        Period = result.Period ?? Period;
        if (result.Basis is { } basis)
        {
            ComparedWith = basis.ComparedWith;
            Analysis = basis.Label;
        }

        var projects = result.Refs.Where(r => r.Type == ProtonRefType.Project).Distinct().ToList();
        if (projects.Count == 1)
        {
            Project = projects[0];
        }

        var clients = result.Refs.Where(r => r.Type == ProtonRefType.Client).Distinct().ToList();
        if (clients.Count == 1)
        {
            Client = clients[0];
        }

        var people = result.Refs.Where(r => r.Type == ProtonRefType.Person).DistinctBy(r => r.Id).ToList();
        if (people.Count > 0)
        {
            People = people.Take(10).ToList();
        }
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (Project is { } project)
        {
            parts.Add($"project #{project.Id} \"{project.Name}\"");
        }

        if (Client is { } client)
        {
            parts.Add($"client #{client.Id} \"{client.Name}\"");
        }

        if (People.Count > 0)
        {
            parts.Add("people " + string.Join(", ", People.Select(p => $"#{p.Id} {p.Name}")));
        }

        if (Period is { } period)
        {
            parts.Add($"period {period.From:yyyy-MM-dd} to {period.To:yyyy-MM-dd}");
        }

        if (ComparedWith is { } compared)
        {
            parts.Add($"compared with {compared.From:yyyy-MM-dd} to {compared.To:yyyy-MM-dd}");
        }

        if (Analysis is { } analysis)
        {
            parts.Add($"last analysis: {analysis}");
        }

        if (Topic is { } topic)
        {
            parts.Add($"last looked up with {topic}");
        }

        return parts.Count == 0 ? "nothing yet" : string.Join("; ", parts);
    }
}
