using System.Collections.Concurrent;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// The tickets of sent pushes whose receipts haven't been checked yet, with the device each went to. In memory: after a
/// restart the unchecked ones are forgotten, and a dead device is caught at its next push instead.
/// </summary>
public sealed class PushReceipts
{
    public const int MaxPending = 50_000;

    private readonly ConcurrentQueue<(string TicketId, string PushToken, DateTime SentAt)> _pending = new();

    public void Add(string ticketId, string pushToken, DateTime sentAt)
    {
        _pending.Enqueue((ticketId, pushToken, sentAt));
        while (_pending.Count > MaxPending && _pending.TryDequeue(out _))
        {
        }
    }

    /// <summary>Removes and returns the tickets sent before <paramref name="cutoff"/>, oldest first.</summary>
    public IReadOnlyList<(string TicketId, string PushToken)> TakeSentBefore(DateTime cutoff)
    {
        var due = new List<(string, string)>();
        while (_pending.TryPeek(out var next) && next.SentAt < cutoff && _pending.TryDequeue(out next))
        {
            due.Add((next.TicketId, next.PushToken));
        }

        return due;
    }
}
