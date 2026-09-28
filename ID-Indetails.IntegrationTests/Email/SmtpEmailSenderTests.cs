using System.Net.Http.Json;
using System.Text.Json;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Email;

/// <summary>
/// Sends a real email over SMTP to the local Mailpit container and reads it back through
/// Mailpit's API, proving the MailKit sender works end to end. Needs `docker compose up -d`.
/// </summary>
public class SmtpEmailSenderTests
{
    private const string MailpitApi = "http://127.0.0.1:8025/api/v1";

    [Fact]
    public async Task Email_is_delivered_to_mailpit_with_both_html_and_text_bodies()
    {
        var subject = $"SMTP test {Guid.NewGuid():N}";
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "127.0.0.1",
            Port = 1025,
            FromAddress = "no-reply@id-indetails.local",
            FromName = "Hourline",
        }));

        await sender.SendAsync(new EmailMessage(
            "recipient@test.local", "Recipient", subject, "<p>Hello <strong>there</strong></p>", "Hello there"));

        using var http = new HttpClient();
        var list = await http.GetFromJsonAsync<JsonElement>($"{MailpitApi}/messages?limit=50");
        var message = list.GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("Subject").GetString() == subject);

        message.GetProperty("From").GetProperty("Address").GetString().ShouldBe("no-reply@id-indetails.local");
        message.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("recipient@test.local");

        var full = await http.GetFromJsonAsync<JsonElement>($"{MailpitApi}/message/{message.GetProperty("ID").GetString()}");
        full.GetProperty("HTML").GetString()!.ShouldContain("<strong>there</strong>");
        full.GetProperty("Text").GetString()!.Trim().ShouldBe("Hello there");
    }
}
