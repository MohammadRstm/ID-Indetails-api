using System.Net;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>The emails that carry an invoice or a statement to the client. Every value is HTML-encoded.</summary>
public static class DocumentEmails
{
    public static EmailMessage Invoice(string to, InvoiceResponse invoice, string companyName, string senderName, PdfFile pdf)
    {
        var due = invoice.DueOn is { } day ? "due by " + PdfLayout.Date(day) : "due on receipt";
        var amount = PdfLayout.Money(invoice.Balance ?? invoice.Total, invoice.Currency);
        return Message(
            to,
            $"Invoice {invoice.Number} - {invoice.Project.Title}",
            invoice.BillToName ?? invoice.Client.DisplayName,
            $"Please find invoice {invoice.Number} for {invoice.Project.Title} attached: {amount}, {due}.",
            "Bank details are on the invoice. Thank you.",
            senderName,
            companyName,
            pdf);
    }

    public static EmailMessage Statement(string to, StatementResponse statement, string companyName, string senderName, PdfFile pdf)
    {
        var left = statement.PaidAhead > 0
            ? "paid in advance " + PdfLayout.Money(statement.PaidAhead, statement.Currency)
            : "remaining " + PdfLayout.Money(statement.Remaining, statement.Currency);
        return Message(
            to,
            $"Materials statement {statement.Number} - {statement.ProjectTitle}",
            statement.Client.DisplayName,
            $"Please find the materials statement {statement.Number} for {statement.ProjectTitle} attached: materials "
                + $"{PdfLayout.Money(statement.Total, statement.Currency)}, received so far "
                + $"{PdfLayout.Money(statement.Received, statement.Currency)}, {left}.",
            "This statement is for your information. It is not an invoice.",
            senderName,
            companyName,
            pdf);
    }

    private static EmailMessage Message(
        string to, string subject, string greeting, string body, string closing, string sender, string company, PdfFile pdf)
    {
        var html = $"""
            <p>Hello {Encode(greeting)},</p>
            <p>{Encode(body)}</p>
            <p>{Encode(closing)}<br>{Encode(sender)} · {Encode(company)}</p>
            """;

        var text = $"""
            Hello {greeting},

            {body}

            {closing}
            {sender} · {company}
            """;

        return new EmailMessage(to, null, subject, html, text, [new EmailAttachment(pdf.FileName, "application/pdf", pdf.Content)]);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
