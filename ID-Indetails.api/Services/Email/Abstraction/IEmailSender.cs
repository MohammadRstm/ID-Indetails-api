namespace ID_Indetails.api.Services.Email.Abstraction;

public sealed record EmailAttachment(string FileName, string MimeType, byte[] Content);

public sealed record EmailMessage(
    string ToAddress,
    string? ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public interface IEmailSender
{
    /// <summary>Sends the message or throws. Callers decide whether a failure rolls back their work.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
