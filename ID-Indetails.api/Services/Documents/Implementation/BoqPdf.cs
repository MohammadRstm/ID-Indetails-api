using System.Globalization;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Boqs.Abstraction;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// A client BOQ or variation order. The client reads each line's final quantity, unit, rate and total, then the
/// subtotal, adjustment, total, supervision, TVA and what to pay. The exact quantity and the waste are never shown.
/// </summary>
public static class BoqPdf
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Label(BoqKind kind) => kind == BoqKind.Vo ? "Variation order" : "Bill of quantities";

    public static byte[] Render(BoqDocument document, DateOnly date, Letterhead head)
    {
        var boq = document.Boq;
        var label = Label(boq.Kind);
        var number = boq.Number ?? "Draft";
        var (pdf, section) = PdfLayout.NewDocument($"{label} {number}", $"{head.CompanyName} · {label} {number}");
        PdfLayout.AddLetterhead(section, head, label, number, boq.Status == BoqStatus.Draft ? "DRAFT" : null);
        PdfLayout.AddFacts(
            section,
            [new Fact("Prepared for", document.ClientName)],
            [new Fact("Project", document.ProjectTitle, document.ProjectLocation)],
            [new Fact("Date", PdfLayout.Date(date))]);
        PdfLayout.AddTitle(section, boq.Title, $"{label} {number}");

        if (boq.Kind == BoqKind.Vo && boq.Reason is { } reason)
        {
            PdfLayout.AddHeading(section, "Reason");
            PdfLayout.AddParagraph(section, reason);
        }

        PdfLayout.AddHeading(section, "Items");
        var table = PdfLayout.AddTable(
            section,
            new TableColumn("No.", 1.8),
            new TableColumn("Description", 7.2),
            new TableColumn("Quantity", 2.2, AlignRight: true),
            new TableColumn("Unit", 1.6),
            new TableColumn("Rate", 2.2, AlignRight: true),
            new TableColumn("Total", 2.4, AlignRight: true));
        foreach (var line in boq.Lines)
        {
            PdfLayout.AddRow(table, false, line.Code, line.Description, Quantity(line.FinalQuantity), line.Unit, Rate(line.Rate), PdfLayout.Money(line.TotalUsd));
        }

        PdfLayout.AddTotals(section, [.. Totals(boq)]);
        if (boq.Status == BoqStatus.Draft)
        {
            PdfLayout.AddNote(section, "This is a draft for your review. It is final once it is agreed.", spaceBeforePt: 18);
        }

        return PdfLayout.Render(pdf);
    }

    /// <summary>Subtotal and adjustment (when there is one), total, supervision, TVA and what the client pays.</summary>
    public static IReadOnlyList<TotalRow> Totals(BoqDetailResponse boq)
    {
        var rows = new List<TotalRow>();
        if (boq.AdjustmentType != BoqAdjustmentType.None)
        {
            rows.Add(new TotalRow("Subtotal", PdfLayout.Money(boq.SubtotalUsd)));
            rows.Add(new TotalRow(
                boq.AdjustmentType == BoqAdjustmentType.Percent && boq.AdjustmentValue is { } fraction ? $"Adjustment ({Percent(fraction)})" : "Adjustment",
                PdfLayout.Money(boq.AdjustmentUsd)));
        }

        rows.Add(new TotalRow("Total", PdfLayout.Money(boq.TotalUsd), Strong: true));
        // the PDF is only made for whoever reads client BOQs, who is always given these
        rows.Add(new TotalRow($"Supervision ({Percent(boq.SupervisionRate!.Value)})", PdfLayout.Money(boq.SupervisionUsd!.Value)));
        rows.Add(new TotalRow($"TVA ({Percent(boq.TvaRate!.Value)})", PdfLayout.Money(boq.TvaUsd!.Value)));
        rows.Add(new TotalRow("Total to pay", PdfLayout.Money(boq.ClientTotalUsd!.Value), Strong: true));
        return rows;
    }

    /// <summary>12,345.5: up to 2 decimals.</summary>
    public static string Quantity(decimal quantity) => quantity.ToString("#,##0.##", Invariant);

    /// <summary>$12.50, or $0.0125: at least 2 decimals, up to the 4 a rate is kept in.</summary>
    public static string Rate(decimal rate) => (rate < 0 ? "-$" : "$") + Math.Abs(rate).ToString("#,##0.00##", Invariant);

    /// <summary>A fraction as a percentage: 0.11 is 11%.</summary>
    public static string Percent(decimal fraction) => (fraction * 100).ToString("0.##", Invariant) + "%";
}
