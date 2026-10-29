using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// A quote: what the whole project costs the client, TVA included and never shown apart, with the project's
/// description as its scope. An Execution site's materials are billed as they are delivered, so they aren't in it.
/// </summary>
public static class QuotePdf
{
    public const string MaterialsNote = "Materials are billed separately, at their prices, as they are delivered to the site.";

    public static byte[] Render(QuoteResponse quote, DateOnly quotedOn, Letterhead head)
    {
        var (document, section) = PdfLayout.NewDocument($"Quote {quote.Number}", $"{head.CompanyName} · Quote {quote.Number}");
        PdfLayout.AddLetterhead(section, head, "Quote", quote.Number, null);
        PdfLayout.AddFacts(
            section,
            [new Fact("Prepared for", quote.Client.DisplayName)],
            [new Fact("Project", quote.ProjectTitle, quote.ProjectLocation)],
            [new Fact("Date", PdfLayout.Date(quotedOn))]);

        if (quote.Scope is { } scope)
        {
            PdfLayout.AddHeading(section, "Scope");
            PdfLayout.AddParagraph(section, scope);
        }

        PdfLayout.AddHeading(section, "Price");
        PdfLayout.AddTotals(section, new TotalRow("Total for the project", PdfLayout.Money(quote.TotalUsd, "USD"), Strong: true));
        if (quote.Domain == DomainCodes.Execution)
        {
            PdfLayout.AddNote(section, MaterialsNote);
        }

        PdfLayout.AddNote(section, "Payments are invoiced as the project goes. Each invoice shows what is paid so far and what is left.", spaceBeforePt: 18);
        return PdfLayout.Render(document);
    }
}
