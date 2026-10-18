using System.Globalization;
using ID_Indetails.api.Services.Email.Abstraction;
using static ID_Indetails.api.Services.Email.Implementation.EmailLayout;

namespace ID_Indetails.api.Services.Email.Implementation;

/// <summary>A message someone sent through the public website's contact form.</summary>
public sealed record WebsiteContactMessage(string Name, string Email, string? Phone, string Message, DateTime SentAtUtc);

/// <summary>Emails to the company about its public website. Every visitor-supplied value is HTML-encoded.</summary>
public static class WebsiteEmails
{
    /// <summary>To the company's own address; replying answers the visitor.</summary>
    public static EmailMessage ContactMessage(string productName, string toAddress, WebsiteContactMessage message)
    {
        var subject = $"Website message from {message.Name}";
        var when = message.SentAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var html = Title($"A message from {message.Name}")
            + Facts(("Name", message.Name), ("Email", message.Email), ("Phone", message.Phone), ("Sent", when))
            + Heading("Message")
            + Quote(message.Message);
        var phone = message.Phone is null ? "" : $"Phone: {message.Phone}\n";
        var text = $"A message from {message.Name} through the website.\n\nEmail: {message.Email}\n{phone}Sent: {when}\n\n{message.Message}\n";

        return Compose(toAddress, null, subject, Page(productName, subject, html, "Sent from the contact form on the company website. Reply to answer them."), text)
            with { ReplyToAddress = message.Email, ReplyToName = message.Name };
    }
}
