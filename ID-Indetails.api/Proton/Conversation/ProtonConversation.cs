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

        if (Topic is { } topic)
        {
            parts.Add($"last looked up with {topic}");
        }

        return parts.Count == 0 ? "nothing yet" : string.Join("; ", parts);
    }
}
