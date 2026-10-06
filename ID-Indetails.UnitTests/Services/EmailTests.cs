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
}
