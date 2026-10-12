using System.Collections.Concurrent;
using ID_Indetails.api.Services.Push.Abstraction;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Records pushes instead of sending them to Expo. Tokens in <see cref="Unregistered"/> answer as Expo does for a removed app.</summary>
public sealed class CapturingPushProvider : IPushProvider
{
    private readonly ConcurrentQueue<PushMessage> _sent = new();

    public ConcurrentDictionary<string, bool> Unregistered { get; } = new();

    public Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            _sent.Enqueue(message);
        }

        IReadOnlyList<PushTicket> tickets = messages
            .Select(m => Unregistered.ContainsKey(m.To) ? new PushTicket(null, IPushProvider.DeviceNotRegistered) : new PushTicket(Guid.NewGuid().ToString(), null))
            .ToList();
        return Task.FromResult(tickets);
    }

    public Task<IReadOnlyDictionary<string, string>> ReceiptErrorsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    public IReadOnlyList<PushMessage> SentTo(string pushToken) => _sent.Where(m => m.To == pushToken).ToList();

    /// <summary>The first push to <paramref name="pushToken"/> that matches, waiting for the background sender up to 10 seconds.</summary>
    public async Task<PushMessage> WaitForAsync(string pushToken, Func<PushMessage, bool> match)
    {
        for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(10); waited += TimeSpan.FromMilliseconds(100))
        {
            if (SentTo(pushToken).FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new ShouldAssertException($"No matching push reached {pushToken}; it got {SentTo(pushToken).Count}.");
    }
}
