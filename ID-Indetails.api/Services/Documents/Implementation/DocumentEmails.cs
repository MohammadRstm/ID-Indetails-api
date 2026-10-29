using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using static ID_Indetails.api.Services.Email.Implementation.EmailLayout;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// The emails that carry an invoice, a quote or a BOQ to the client. The email shows the document itself, as the PDF
/// does (only client-facing fields), and the PDF is attached to keep or print. Every value is HTML-encoded.
/// </summary>
public static class DocumentEmails
{
    /// <param name="issuedOn">The issue day in the company time zone.</param>
    /// <param name="billing">The business's bank details and wording; null when they aren't set up.</param>
    public static EmailMessage Invoice(
        string to, InvoiceResponse invoice, DateOnly? issuedOn, Letterhead head, BillingSettings? billing, string senderName, PdfFile pdf)
    {
        var amount = Money(invoice.Balance ?? invoice.Total, invoice);
        var due = invoice.DueOn is { } day ? "due " + PdfLayout.Date(day) : "due on receipt";
        var intro = $"Please find below invoice {invoice.Number} for {invoice.Project.Title}. A PDF copy is attached for your records.";
        var html = InvoiceBody(invoice, issuedOn, billing, intro, Highlight("Amount due", amount, char.ToUpperInvariant(due[0]) + due[1..]))
            + SignOff("Thank you for your business,", senderName, head.CompanyName);
        var text = $"""
            Hello {Greeting(invoice)},

            {intro}

            Amount due: {amount} ({due})
            {InvoiceText(invoice, issuedOn, billing)}
            Thank you for your business,
            {senderName}
            {head.CompanyName}
            """;

        return Compose(
            to, null, $"Invoice {invoice.Number} - {invoice.Project.Title}",
            Page(head.CompanyName, $"Invoice {invoice.Number}: {amount}, {due}.", html, Footer(head, billing)),
            text,
            [Attachment(pdf)]);
    }

    /// <summary>An automatic reminder of an overdue invoice, from the company's accounts.</summary>
    public static EmailMessage InvoiceReminder(
        string to, InvoiceResponse invoice, DateOnly? issuedOn, Letterhead head, BillingSettings? billing, PdfFile pdf)
    {
        var due = invoice.DueOn is { } day ? PdfLayout.Date(day) : "receipt";
        var amount = Money(invoice.Balance ?? invoice.Total, invoice);
        var intro = $"This is a friendly reminder that invoice {invoice.Number} for {invoice.Project.Title} was due on {due}, and {amount} of it is still to be paid. The invoice is below, and attached as a PDF.";
        var settled = "If you have already paid, thank you, and please ignore this message.";
        var html = InvoiceBody(invoice, issuedOn, billing, intro, Highlight("Amount outstanding", amount, "Was due on " + due, badge: "Overdue"))
            + Note(settled)
            + SignOff("Kind regards,", "Accounts", head.CompanyName);
        var text = $"""
            Hello {Greeting(invoice)},

            {intro}
            {InvoiceText(invoice, issuedOn, billing)}
            {settled}

            Kind regards,
            Accounts
            {head.CompanyName}
            """;

        return Compose(
            to, null, $"Reminder: invoice {invoice.Number} - {invoice.Project.Title}",
            Page(head.CompanyName, $"Invoice {invoice.Number} is overdue: {amount} still to be paid.", html, Footer(head, billing)),
            text,
            [Attachment(pdf)]);
    }

    /// <summary>A client BOQ or variation order: its items as the PDF shows them (final quantities, never waste), and its totals.</summary>
    public static EmailMessage Boq(string to, BoqDocument document, DateOnly date, Letterhead head, string senderName, PdfFile pdf)
    {
        var boq = document.Boq;
        var label = BoqPdf.Label(boq.Kind);
        var number = boq.Number ?? "Draft";
        var total = PdfLayout.Money(boq.ClientTotalUsd);
        var intro = $"Please find below the {label.ToLowerInvariant()} {number} for {document.ProjectTitle}. A PDF copy is attached for your records.";
        var rows = boq.Lines
            .Select(l => new[]
            {
                l.Code ?? "",
                l.Description,
                BoqPdf.Quantity(l.FinalQuantity) + (string.IsNullOrWhiteSpace(l.Unit) ? "" : " " + l.Unit),
                BoqPdf.Rate(l.Rate),
                PdfLayout.Money(l.TotalUsd),
            })
            .ToList();
        var totals = BoqPdf.Totals(boq);

        var html = Paragraph($"Hello {document.ClientName},")
            + Paragraph(intro)
            + Facts((label, number), ("Title", boq.Title), ("Date", PdfLayout.Date(date)), ("Project", document.ProjectTitle), ("Location", document.ProjectLocation))
            + (boq.Kind == BoqKind.Vo && boq.Reason is { } reason ? Heading("Reason") + Quote(reason) : "")
            + Heading("Items") + Table(["No.", "Description", "Quantity", "Rate", "Total"], rows, 2, 3, 4)
            + Totals([.. totals.Select(t => (t.Label, t.Value, t.Strong))])
            + (boq.Status == BoqStatus.Draft ? Note("This is a draft for your review. It is final once it is agreed.") : "")
            + SignOff("Kind regards,", senderName, head.CompanyName);

        var lines = string.Join("\n", boq.Lines.Select(l =>
            $"- {(string.IsNullOrWhiteSpace(l.Code) ? "" : l.Code + " ")}{l.Description}: {BoqPdf.Quantity(l.FinalQuantity)}{(string.IsNullOrWhiteSpace(l.Unit) ? "" : " " + l.Unit)} x {BoqPdf.Rate(l.Rate)} = {PdfLayout.Money(l.TotalUsd)}"));
        var sums = string.Join("\n", totals.Select(t => $"{t.Label}: {t.Value}"));
        var text = $"""
            Hello {document.ClientName},

            {intro}

            {boq.Title}
            {(boq.Kind == BoqKind.Vo && boq.Reason is { } why ? $"Reason: {why}\n" : "")}
            {lines}

            {sums}

            Kind regards,
            {senderName}
            {head.CompanyName}
            """;

        return Compose(
            to, null, $"{label} {number} - {document.ProjectTitle}",
            Page(head.CompanyName, $"{label} {number}: {total} to pay for {document.ProjectTitle}.", html, Footer(head, null)),
            text,
            [Attachment(pdf)]);
    }

    /// <summary>A quote: the project's total for the client, TVA included and never shown apart, with its scope.</summary>
    public static EmailMessage QuoteEmail(string to, QuoteResponse quote, DateOnly quotedOn, Letterhead head, string senderName, PdfFile pdf)
    {
        var total = PdfLayout.Money(quote.TotalUsd, "USD");
        var intro = $"Please find below our quote {quote.Number} for {quote.ProjectTitle}. A PDF copy is attached.";
        var html = Paragraph($"Hello {quote.Client.DisplayName},")
            + Paragraph(intro)
            + Facts(("Quote", quote.Number), ("Date", PdfLayout.Date(quotedOn)), ("Project", quote.ProjectTitle), ("Location", quote.ProjectLocation))
            + (quote.Scope is { } scope ? Heading("Scope") + Quote(scope) : "")
            + Heading("Price")
            + Totals(("Total for the project", total, true))
            + SignOff("Kind regards,", senderName, head.CompanyName);
        var text = $"""
            Hello {quote.Client.DisplayName},

            {intro}

            {(quote.Scope is { } s ? $"Scope: {s}\n" : "")}Total for the project: {total}

            Kind regards,
            {senderName}
            {head.CompanyName}
            """;
        return Compose(
            to, null, $"Quote {quote.Number} - {quote.ProjectTitle}",
            Page(head.CompanyName, $"Quote {quote.Number}: {total} for {quote.ProjectTitle}.", html, Footer(head, null)),
            text,
            [Attachment(pdf)]);
    }

    /// <summary>The invoice as the PDF shows it: who and what it is for, its lines, totals, notes and how to pay.</summary>
    private static string InvoiceBody(InvoiceResponse invoice, DateOnly? issuedOn, BillingSettings? billing, string intro, string highlight)
    {
        var html = Paragraph($"Hello {Greeting(invoice)},")
            + Paragraph(intro)
            + highlight
            + Facts(
                ("Invoice", invoice.Number),
                ("Issue date", issuedOn is { } issued ? PdfLayout.Date(issued) : null),
                ("Billed to", invoice.BillToName ?? invoice.Client.DisplayName),
                ("Project", invoice.Project.Title))
            + Heading("Details")
            + Table(["Description", "Amount"], invoice.Lines.Select(l => new[] { l.Description, Money(l.Amount, invoice) }), 1)
            + Totals([.. InvoiceTotals(invoice)]);

        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            html += Heading("Notes") + Quote(invoice.Notes);
        }

        var bank = Bank(billing);
        html += Heading("How to pay");
        if (bank.Length > 0)
        {
            html += Facts(bank);
        }

        return html + Note(PaymentTerms(invoice, billing));
    }

    private static string InvoiceText(InvoiceResponse invoice, DateOnly? issuedOn, BillingSettings? billing)
    {
        var lines = string.Join("\n", invoice.Lines.Select(l => $"- {l.Description}: {Money(l.Amount, invoice)}"));
        var totals = string.Join("\n", InvoiceTotals(invoice).Select(t => $"{t.Label}: {t.Value}"));
        var bank = string.Join("\n", Bank(billing).Select(b => $"{b.Label}: {b.Value}"));
        var issued = issuedOn is { } day ? $", issued {PdfLayout.Date(day)}" : "";
        var notes = string.IsNullOrWhiteSpace(invoice.Notes) ? "" : $"\nNotes: {invoice.Notes}\n";
        return $"""

            Invoice {invoice.Number} for {invoice.Project.Title}{issued}
            {lines}
            {totals}
            {notes}
            How to pay:
            {(bank.Length > 0 ? bank + "\n" : "")}{PaymentTerms(invoice, billing)}

            """;
    }

    private static (string Label, string Value, bool Strong)[] InvoiceTotals(InvoiceResponse invoice)
    {
        var rows = new List<(string, string, bool)> { ("Total", Money(invoice.Total, invoice), false) };
        if (invoice.Paid is > 0)
        {
            rows.Add(("Received to date", Money(-invoice.Paid.Value, invoice), false));
        }

        rows.Add(("Amount due", Money(invoice.Balance ?? invoice.Total, invoice), true));
        return [.. rows];
    }

    private static (string Label, string? Value)[] Bank(BillingSettings? billing)
        => new (string Label, string? Value)[]
            {
                ("Account name", billing?.BankAccount),
                ("Bank", billing?.BankName),
                ("IBAN", billing?.Iban),
                ("SWIFT", billing?.Swift),
            }
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .ToArray();

    private static string PaymentTerms(InvoiceResponse invoice, BillingSettings? billing)
    {
        var terms = invoice.TermsDays > 0 ? $"Payment is due within {invoice.TermsDays} days of the invoice date." : "Payment is due on receipt.";
        return $"{terms} Please quote invoice {invoice.Number} with your transfer. {billing?.TermsText}".Trim();
    }

    private static string?[] Footer(Letterhead head, BillingSettings? billing)
        => [billing?.Footer, string.Join(" · ", new[] { head.CompanyName, head.Address, head.Website }.Where(s => !string.IsNullOrWhiteSpace(s)))];

    private static string Greeting(InvoiceResponse invoice) => invoice.BillToName ?? invoice.Client.DisplayName;

    private static string Money(decimal amount, InvoiceResponse invoice) => PdfLayout.Money(amount, invoice.Currency);

    private static EmailAttachment Attachment(PdfFile pdf) => new(pdf.FileName, "application/pdf", pdf.Content);
}
