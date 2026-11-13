using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ID_Indetails.api.Services.Email.Abstraction;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Records emails instead of sending them, and can be told to fail.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public bool FailNextSend { get; set; }

    /// <summary>Fails only the next email to this address; the others still go.</summary>
    public string? FailNextSendTo { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var failTo = FailNextSendTo is { } to && message.ToAddress.Equals(to, StringComparison.OrdinalIgnoreCase);
        if (FailNextSend || failTo)
        {
            FailNextSend = false;
            if (failTo)
            {
                FailNextSendTo = null;
            }

            throw new InvalidOperationException("Simulated SMTP failure.");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage LastTo(string address)
        => _sent.Reverse().FirstOrDefault(m => m.ToAddress.Equals(address, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"No email was sent to {address}.");

    public int CountTo(string address)
        => _sent.Count(m => m.ToAddress.Equals(address, StringComparison.OrdinalIgnoreCase));

    /// <summary>The token from the signup link in the latest invitation to <paramref name="address"/>.</summary>
    public string InvitationTokenFor(string address)
        => Uri.UnescapeDataString(TokenInLink().Match(LastTo(address).TextBody).Groups[1].Value);

    /// <summary>The token from the link in the latest password reset email to <paramref name="address"/>.</summary>
    public string ResetTokenFor(string address)
        => Uri.UnescapeDataString(TokenInLink().Match(LastTo(address).TextBody).Groups[1].Value);

    /// <summary>The temporary password from the latest super-admin email to <paramref name="address"/>.</summary>
    public string TemporaryPasswordFor(string address)
        => TemporaryPassword().Match(LastTo(address).TextBody).Groups[1].Value;

    [GeneratedRegex(@"[?&]token=([^\s&]+)")]
    private static partial Regex TokenInLink();

    [GeneratedRegex(@"temporary password:\s+(\S+)")]
    private static partial Regex TemporaryPassword();
}
