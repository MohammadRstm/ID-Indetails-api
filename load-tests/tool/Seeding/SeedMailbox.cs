using System.Text.RegularExpressions;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.LoadTests.Tool.Safety;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// The API's mail while seeding: kept in memory, never sent, so seeding needs no mail server at all. It refuses any
/// recipient outside the reserved domain, which fails the request and stops the seed.
/// </summary>
public sealed partial class SeedMailbox : IEmailSender
{
    private readonly Dictionary<string, EmailMessage> _latest = new(StringComparer.OrdinalIgnoreCase);

    public int Received { get; private set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!message.ToAddress.EndsWith("@" + EmailSafety.Domain, StringComparison.OrdinalIgnoreCase)
            || EmailSafety.HasDeliverableAddress(message.ToAddress))
        {
            throw new InvalidOperationException($"The API tried to email {message.ToAddress}, outside @{EmailSafety.Domain}.");
        }

        _latest[message.ToAddress] = message;
        Received++;
        return Task.CompletedTask;
    }

    /// <summary>The token in the sign-up link of the latest invitation to that address.</summary>
    public string InvitationToken(string address)
        => Uri.UnescapeDataString(Find(TokenInLink(), address));

    /// <summary>The generated password in the latest super-admin email to that address.</summary>
    public string TemporaryPassword(string address) => Find(TemporaryPasswordLine(), address);

    private string Find(Regex pattern, string address)
    {
        var match = _latest.TryGetValue(address, out var message) ? pattern.Match(message.TextBody) : Match.Empty;
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"No email to {address} holds what seeding needs.");
    }

    [GeneratedRegex(@"[?&]token=([^\s&]+)")]
    private static partial Regex TokenInLink();

    [GeneratedRegex(@"temporary password:\s+(\S+)")]
    private static partial Regex TemporaryPasswordLine();
}
