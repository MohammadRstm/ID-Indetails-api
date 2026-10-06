namespace ID_Indetails.api.Services.Email.Abstraction;

public sealed record EmailAttachment(string FileName, string MimeType, byte[] Content);

/// <summary>An image the HTML shows in place (src="cid:{ContentId}"), carried in the email rather than fetched from the web.</summary>
public sealed record EmailInlineImage(string ContentId, string MimeType, byte[] Content);

public sealed record EmailMessage(
    string ToAddress,
    string? ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    IReadOnlyList<EmailInlineImage>? InlineImages = null);

public interface IEmailSender
{
    /// <summary>Sends the message or throws. Callers decide whether a failure rolls back their work.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
