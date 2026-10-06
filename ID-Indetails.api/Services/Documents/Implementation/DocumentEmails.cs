using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using static ID_Indetails.api.Services.Email.Implementation.EmailLayout;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// The emails that carry an invoice or a statement to the client. The email shows the document itself, as the PDF
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

    public static EmailMessage Statement(string to, StatementResponse statement, Letterhead head, string senderName, PdfFile pdf)
    {
        string M(decimal amount) => PdfLayout.Money(amount, statement.Currency);
        var split = statement.Mode == StatementMode.Detail && statement.IncludesEarlier;
        var intro = $"Please find below the materials statement {statement.Number} for {statement.ProjectTitle}. A PDF copy is attached for your records.";
        var notInvoice = "This statement is for your information. It is not an invoice.";
        var left = statement.PaidAhead > 0 ? ("Paid in advance", M(statement.PaidAhead)) : ("Remaining amount", M(statement.Remaining));

        string table;
        if (statement.Mode == StatementMode.Total)
        {
            var since = statement.Since is { } day ? " since " + PdfLayout.Date(day) : " to date";
            table = Table(["Description", "Amount"], [[$"Materials supplied{since} ({Items(statement.Count)})", M(statement.Total)]], 1);
        }
        else if (split)
        {
            var rows = statement.Types.Select(t => new[] { t.Label, t.Quantity ?? "-", Amount(t.Earlier, M), Amount(t.New, M), M(t.Total) }).ToList();
            if (Hidden(statement) is { } hidden)
            {
                rows.Add([hidden, "", "", "", M(statement.HiddenTypes!.Total)]);
            }

            table = Table(["Material", "Quantity", "Earlier", "New", "Total"], rows, 2, 3, 4);
        }
        else
        {
            var rows = statement.Types.Select(t => new[] { t.Label, t.Quantity ?? "-", M(t.Total) }).ToList();
            if (Hidden(statement) is { } hidden)
            {
                rows.Add([hidden, "", M(statement.HiddenTypes!.Total)]);
            }

            table = Table(["Material", "Quantity", "Amount"], rows, 2);
        }

        var totals = new List<(string, string, bool)>();
        if (split)
        {
            totals.Add(("On earlier statements", M(statement.EarlierTotal), false));
            totals.Add(("New this time", M(statement.NewTotal), false));
        }

        totals.Add(("Materials total", M(statement.Total), true));

        var html = Paragraph($"Hello {statement.Client.DisplayName},")
            + Paragraph(intro)
            + Facts(("Statement", statement.Number), ("Date", PdfLayout.Date(statement.Date)), ("Project", statement.ProjectTitle), ("Location", statement.ProjectLocation))
            + Heading("Materials") + table + Totals([.. totals])
            + Heading("Payment summary")
            + Totals(("Received from you so far", M(statement.Received), false), (left.Item1, left.Item2, true))
            + Note("The remaining amount is what is left of everything agreed on this project so far, after the payments above.")
            + Note(notInvoice)
            + SignOff("Kind regards,", senderName, head.CompanyName);

        var lines = statement.Mode == StatementMode.Total
            ? $"Materials supplied ({Items(statement.Count)}): {M(statement.Total)}"
            : string.Join("\n", statement.Types.Select(t => $"- {t.Label}{(t.Quantity is null ? "" : $" ({t.Quantity})")}: {M(t.Total)}"))
              + $"\nMaterials total: {M(statement.Total)}";
        var text = $"""
            Hello {statement.Client.DisplayName},

            {intro}

            {lines}
            Received from you so far: {M(statement.Received)}
            {left.Item1}: {left.Item2}

            {notInvoice}

            Kind regards,
            {senderName}
            {head.CompanyName}
            """;

        return Compose(
            to, null, $"Materials statement {statement.Number} - {statement.ProjectTitle}",
            Page(head.CompanyName, $"Materials statement {statement.Number}: materials {M(statement.Total)}, {left.Item1.ToLowerInvariant()} {left.Item2}.", html, Footer(head, null)),
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

    private static string Amount(decimal amount, Func<decimal, string> money) => amount == 0 ? "-" : money(amount);

    private static string Items(int count) => count == 1 ? "1 item" : $"{count} items";

    private static string? Hidden(StatementResponse statement)
        => statement.HiddenTypes is { Count: > 0 } hidden
            ? (hidden.Count == 1 ? "1 more material type" : $"{hidden.Count} more material types") + " from earlier statements, not listed again"
            : null;

    private static EmailAttachment Attachment(PdfFile pdf) => new(pdf.FileName, "application/pdf", pdf.Content);
}
