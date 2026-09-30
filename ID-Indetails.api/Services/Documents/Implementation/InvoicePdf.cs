using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// The invoice the client receives. It reads only client-facing fields: never rates, labour, hours, expenses,
/// profit or margin.
/// </summary>
public static class InvoicePdf
{
    /// <param name="issuedOn">The issue day in the company time zone; null for a draft.</param>
    /// <param name="billing">The domain's bank details and wording; null when they aren't set up.</param>
    public static byte[] Render(InvoiceResponse invoice, DateOnly? issuedOn, Letterhead head, BillingSettings? billing)
    {
        var number = invoice.Number ?? "Draft";
        var (document, section) = PdfLayout.NewDocument($"Invoice {number}", $"{head.CompanyName} · Invoice {number}");
        var stamp = invoice.VoidedAt is not null ? "VOID" : invoice.Number is null ? "DRAFT" : null;
        PdfLayout.AddLetterhead(section, head, "Invoice", number, stamp);

        PdfLayout.AddFacts(
            section,
            [new Fact("Billed to", invoice.BillToName ?? invoice.Client.DisplayName, invoice.BillToContact)],
            [new Fact("Project", invoice.Project.Title, invoice.Project.Location)],
            [
                new Fact("Issue date", issuedOn is { } issued ? PdfLayout.Date(issued) : "Not issued"),
                new Fact("Due date", invoice.DueOn is { } due ? PdfLayout.Date(due) : Terms(invoice.TermsDays)),
            ]);

        PdfLayout.AddHeading(section, "Details");
        var lines = PdfLayout.AddTable(section, new TableColumn("Description", 13.4), new TableColumn("Amount", 4, AlignRight: true));
        foreach (var line in invoice.Lines)
        {
            PdfLayout.AddRow(lines, false, line.Description, PdfLayout.Money(line.Amount, invoice.Currency));
        }

        var totals = new List<TotalRow>
        {
            new("Subtotal", PdfLayout.Money(invoice.Total, invoice.Currency)),
            new("Total", PdfLayout.Money(invoice.Total, invoice.Currency), Strong: true),
        };
        if (invoice.Paid is > 0)
        {
            totals.Add(new("Received to date", PdfLayout.Money(-invoice.Paid.Value, invoice.Currency)));
        }

        totals.Add(new("Amount due", PdfLayout.Money(invoice.Balance ?? invoice.Total, invoice.Currency), Strong: true));
        PdfLayout.AddTotals(section, [.. totals]);

        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            PdfLayout.AddHeading(section, "Notes");
            PdfLayout.AddParagraph(section, invoice.Notes);
        }

        PdfLayout.AddHeading(section, "How to pay");
        var bank = new[]
            {
                new Fact("Account name", billing?.BankAccount ?? string.Empty),
                new Fact("Bank", billing?.BankName ?? string.Empty),
                new Fact("IBAN", billing?.Iban ?? string.Empty),
                new Fact("SWIFT", billing?.Swift ?? string.Empty),
            }
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => (IReadOnlyList<Fact>)[f])
            .ToArray();
        if (bank.Length > 0)
        {
            PdfLayout.AddFacts(section, bank);
        }

        var terms = invoice.TermsDays > 0
            ? $"Payment due within {invoice.TermsDays} days of the invoice date."
            : "Payment due on receipt.";
        PdfLayout.AddNote(section, $"{terms} Please quote the invoice number with your transfer. {billing?.TermsText}".Trim());
        if (!string.IsNullOrWhiteSpace(billing?.Footer))
        {
            PdfLayout.AddNote(section, billing.Footer, spaceBeforePt: 18);
        }

        return PdfLayout.Render(document);
    }

    private static string Terms(int days) => days > 0 ? $"Net {days}" : "On receipt";
}
