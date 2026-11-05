using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// A Design variation order as the client gets it: what changes, line by line before TVA, the TVA at the project's
/// rate and the total, and, when it shows them, the hours worked outside the agreed scope since the last one.
/// </summary>
public static class DesignVoPdf
{
    public const string Label = "Variation order";

    public const string PendingNote = "This variation order is for your approval. The project's price changes once you confirm it.";

    public static byte[] Render(DesignVoDocument document, DateOnly date, Letterhead head)
    {
        var vo = document.Vo;
        var (pdf, section) = PdfLayout.NewDocument($"{Label} {vo.Number}", $"{head.CompanyName} · {Label} {vo.Number}", forClient: true);
        PdfLayout.AddLetterhead(section, head, Label, vo.Number, Stamp(vo.Status));
        PdfLayout.AddFacts(
            section,
            [new Fact("Prepared for", document.ClientName)],
            [new Fact("Project", document.ProjectTitle, document.ProjectLocation)],
            [new Fact("Date", PdfLayout.Date(date))]);
        PdfLayout.AddTitle(section, vo.Title, $"{Label} {vo.Number}");

        PdfLayout.AddHeading(section, "Changes");
        var table = PdfLayout.AddTable(section, new TableColumn("No.", 1.4), new TableColumn("Description", 12.2), new TableColumn("Amount", 3.2, AlignRight: true));
        foreach (var (line, i) in vo.Lines.Select((l, i) => (l, i)))
        {
            PdfLayout.AddRow(table, false, (i + 1).ToString(), line.Description, PdfLayout.Money(line.Amount));
        }

        PdfLayout.AddTotals(section, [.. Totals(vo)]);
        if (OutsideScope(vo) is { } hours)
        {
            PdfLayout.AddHeading(section, "Work outside the agreed scope");
            PdfLayout.AddParagraph(section, hours);
        }

        if (vo.Status == DesignVoStatus.Pending)
        {
            PdfLayout.AddNote(section, PendingNote, spaceBeforePt: 18);
        }

        return PdfLayout.Render(pdf);
    }

    /// <summary>Before TVA, the TVA at its rate, and the total; or, on one without TVA, the total with no TVA on it.</summary>
    public static IReadOnlyList<TotalRow> Totals(DesignVoResponse vo) => vo.TvaRate > 0
        ?
        [
            new("Total before TVA", PdfLayout.Money(vo.TotalUsd)),
            new($"TVA ({BoqPdf.Percent(vo.TvaRate)})", PdfLayout.Money(vo.TvaUsd)),
            new("Total", PdfLayout.Money(vo.TotalWithTvaUsd), Strong: true),
        ]
        :
        [
            new("TVA", "None"),
            new("Total", PdfLayout.Money(vo.TotalUsd), Strong: true),
        ];

    /// <summary>"42h 30m logged outside the agreed scope since Oct 3, 2026." when the VO shows them.</summary>
    public static string? OutsideScope(DesignVoResponse vo)
        => vo.OutsideScopeMinutes is { } minutes && vo.OutsideScopeFrom is { } from
            ? $"{minutes / 60}h {minutes % 60:00}m logged outside the agreed scope since {PdfLayout.Date(from)}."
            : null;

    private static string? Stamp(DesignVoStatus status) => status switch
    {
        DesignVoStatus.Pending => "FOR APPROVAL",
        DesignVoStatus.Cancelled => "CANCELLED",
        _ => null,
    };
}
