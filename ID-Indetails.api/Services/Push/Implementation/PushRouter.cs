using ID_Indetails.api.Services.Push.Abstraction;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>Sends each push through its device's service: Web Push for browsers, Expo for the phone app.</summary>
public sealed class PushRouter(ExpoPushProvider expo, WebPushProvider webPush) : IPushProvider
{
    public async Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        var toBrowsers = messages.Select((m, i) => (m, i)).Where(x => x.m.WebPush is not null).ToList();
        var toPhones = messages.Select((m, i) => (m, i)).Where(x => x.m.WebPush is null).ToList();

        var tickets = new PushTicket[messages.Count];
        await SendThroughAsync(webPush, toBrowsers, tickets, cancellationToken);
        await SendThroughAsync(expo, toPhones, tickets, cancellationToken);
        return tickets;
    }

    /// <summary>Only Expo has receipts.</summary>
    public Task<IReadOnlyDictionary<string, string>> ReceiptErrorsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken = default)
        => expo.ReceiptErrorsAsync(ticketIds, cancellationToken);

    /// <summary>Puts each ticket back at its message's place.</summary>
    private static async Task SendThroughAsync(
        IPushProvider provider,
        List<(PushMessage Message, int Index)> messages,
        PushTicket[] tickets,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var sent = await provider.SendAsync(messages.Select(x => x.Message).ToList(), cancellationToken);
        for (var i = 0; i < messages.Count; i++)
        {
            tickets[messages[i].Index] = i < sent.Count ? sent[i] : new PushTicket(null, null);
        }
    }
}
