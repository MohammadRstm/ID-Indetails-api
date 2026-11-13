using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Documents.Implementation;
using ID_Indetails.api.Services.Email.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class EmailTests
{
    private static readonly Letterhead Head = new("ID InDetails", "Beirut, Lebanon", "id-indetails.test", null);

    private static InvoiceResponse Invoice() => new()
    {
        Id = 7,
        Number = "HL-2026-014",
        Project = new InvoiceProjectResponse(3, "Villa <Haddad>"),
        Client = new InvoiceClientResponse(2, "Haddad Holdings"),
        Currency = "USD",
        Total = 1200m,
        Paid = 200m,
        Balance = 1000m,
        DueOn = new DateOnly(2026, 10, 30),
        TermsDays = 30,
        Lines = [new InvoiceLineResponse(1, 1, "Concept design", 700m), new InvoiceLineResponse(2, 2, "Lighting plan", 500m)],
    };

    [Fact]
    public void An_invoice_email_shows_the_invoice_and_carries_its_pdf_and_the_logo()
    {
        var billing = new BillingSettings { BankName = "Bank Audi", Iban = "LB00 0000" };
        var mail = DocumentEmails.Invoice("client@x.test", Invoice(), new DateOnly(2026, 9, 30), Head, billing, "Nadia Karam", new PdfFile([1], "hl-2026-014.pdf"));

        mail.HtmlBody.ShouldContain("Concept design");
        mail.HtmlBody.ShouldContain("$1,000.00");
        mail.HtmlBody.ShouldContain("LB00 0000");
        mail.HtmlBody.ShouldContain("Villa &lt;Haddad&gt;");
        mail.HtmlBody.ShouldNotContain("Villa <Haddad>");
        mail.TextBody.ShouldContain("- Lighting plan: $500.00");
        mail.TextBody.ShouldContain("Amount due: $1,000.00 (due Oct 30, 2026)");
        mail.Attachments.ShouldNotBeNull().ShouldHaveSingleItem().FileName.ShouldBe("hl-2026-014.pdf");

        // the logo travels in the email, so it shows even where pictures from the web are blocked
        var logo = mail.InlineImages.ShouldNotBeNull().ShouldHaveSingleItem();
        mail.HtmlBody.ShouldContain($"cid:{logo.ContentId}");
        logo.Content.Take(4).ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    [Fact]
    public void An_invitation_has_a_button_and_the_link_spelled_out()
    {
        const string link = "https://app.test/accept-invitation?token=a&b";
        var mail = AccountEmails.Invitation("ID InDetails", "new@x.test", "Nadia Karam", "an employee", link, new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));

        mail.HtmlBody.ShouldContain("Accept invitation");
        mail.HtmlBody.ShouldContain("href=\"https://app.test/accept-invitation?token=a&amp;b\"");
        mail.TextBody.ShouldContain(link);
        mail.InlineImages.ShouldNotBeNull().ShouldHaveSingleItem();
    }

    private static BugReport Message(ContactError? error = null) => new(
        "I tried to <save> the entry.",
        "It was the second time.",
        "/design/projects/4",
        new ContactSender("Nadia Karam", "nadia@x.test", "user 12", "employee · design"),
        "Mozilla/5.0",
        false,
        new StampedTime(new DateTime(2026, 11, 13, 12, 32, 5, DateTimeKind.Utc), new DateTime(2026, 11, 13, 14, 32, 5)),
        error);

    [Fact]
    public void A_message_to_the_developer_shows_the_error_in_utc_as_the_log_has_it_and_replies_go_to_the_sender()
    {
        var error = new ContactError(
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            new StampedTime(new DateTime(2026, 11, 13, 12, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 14, 30, 0)),
            null);
        var mail = SupportEmails.BugReport("ID InDetails", "dev@x.test", Message(error));

        mail.ToAddress.ShouldBe("dev@x.test");
        (mail.ReplyToAddress, mail.ReplyToName).ShouldBe(("nadia@x.test", "Nadia Karam"));
        mail.Subject.ShouldBe("ID InDetails: message from Nadia Karam (ref 4bf92f35)");
        mail.HtmlBody.ShouldContain("I tried to &lt;save&gt; the entry.");
        mail.TextBody.ShouldContain("When: 2026-11-13 12:30:00 UTC (2026-11-13 14:30 company time)");
        mail.TextBody.ShouldContain("Search the log for: 4bf92f35");
        mail.TextBody.ShouldContain("Opened in: a browser tab");
    }

    [Fact]
    public void The_senders_copy_has_their_words_and_the_reference_and_nothing_the_server_adds()
    {
        var error = new ContactError("4bf92f35", new StampedTime(DateTime.UtcNow, new DateTime(2026, 11, 13, 14, 30, 0)), null);
        var copy = SupportEmails.BugReportCopy("ID InDetails", Message(error));

        (copy.ToAddress, copy.ToName).ShouldBe(("nadia@x.test", "Nadia Karam"));
        copy.TextBody.ShouldContain("I tried to <save> the entry.");
        copy.TextBody.ShouldContain("Error reference: 4bf92f35");
        copy.TextBody.ShouldContain("When it happened: 13 Nov 2026, 14:30");
        copy.TextBody.ShouldNotContain("user 12");
        copy.TextBody.ShouldNotContain("Mozilla");
        copy.ReplyToAddress.ShouldBeNull();
    }

    [Theory]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", "4bf92f35")]
    [InlineData("0HN7Q2:00000001", "0HN7Q2:00000001")]
    [InlineData("4bf92f35", "4bf92f35")]
    public void The_reference_a_person_reads_out_is_the_start_of_a_trace_id(string traceId, string reference)
        => SupportEmails.RefOf(traceId).ShouldBe(reference);
}
