using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>The company as it appears at the top of every document.</summary>
/// <param name="Logo">A PNG or JPEG, or null.</param>
public sealed record Letterhead(string CompanyName, string? Address, string? Website, byte[]? Logo);

/// <param name="Note">A smaller line under the value.</param>
public sealed record Fact(string Label, string Value, string? Note = null);

public sealed record TableColumn(string Header, double WidthCm, bool AlignRight = false);

public sealed record TotalRow(string Label, string Value, bool Strong = false);

/// <summary>
/// What every document shares: an A4 page, the fonts, the letterhead, tables and totals, the page footer, and how
/// money and dates are written. Long tables run onto more pages by themselves and repeat their header row.
/// </summary>
public static class PdfLayout
{
    public const double ContentWidthCm = 17.4;

    /// <summary>The width between the margins of an A4 page turned on its side, for wide tables.</summary>
    public const double LandscapeContentWidthCm = 26.1;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Color Ink = new(0x1F, 0x26, 0x25);
    private static readonly Color Muted = new(0x6B, 0x76, 0x74);
    private static readonly Color Line = new(0xDD, 0xE2, 0xE0);
    private static readonly Color Shade = new(0xF3, 0xF5, 0xF4);
    private static readonly Color Alert = new(0xB4, 0x23, 0x18);

    static PdfLayout()
    {
        GlobalFontSettings.FontResolver ??= new DocumentFonts();
    }

    /// <param name="footer">Shown at the bottom of every page, beside the page number.</param>
    /// <param name="forClient">A document the client receives: the company's mark goes behind every page (<see cref="PdfWatermark"/>).</param>
    /// <param name="landscape">A4 on its side, for tables with many columns. Its content is <see cref="LandscapeContentWidthCm"/> wide.</param>
    public static (Document Document, Section Section) NewDocument(string title, string footer, bool forClient = false, bool landscape = false)
    {
        var document = new Document();
        document.Info.Title = title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = DocumentFonts.Family;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(1);

        var foot = section.Footers.Primary.AddParagraph();
        foot.Format.Font.Size = 7.5;
        foot.Format.Font.Color = Muted;
        foot.Format.AddTabStop(Unit.FromCentimeter(landscape ? LandscapeContentWidthCm : ContentWidthCm), TabAlignment.Right);
        foot.AddText(footer);
        foot.AddTab();
        foot.AddText("Page ");
        foot.AddPageField();
        foot.AddText(" of ");
        foot.AddNumPagesField();

        if (forClient)
        {
            PdfWatermark.AddTo(section);
        }

        return (document, section);
    }

    /// <param name="stamp">A word in red under the number, such as VOID or DRAFT.</param>
    /// <param name="widthCm">The width of the page's content: <see cref="LandscapeContentWidthCm"/> on a landscape page.</param>
    public static void AddLetterhead(
        Section section, Letterhead head, string kind, string number, string? stamp = null, double widthCm = ContentWidthCm)
    {
        var table = section.AddTable();
        var hasLogo = head.Logo is not null;
        if (hasLogo)
        {
            table.AddColumn(Unit.FromCentimeter(1.6));
        }

        table.AddColumn(Unit.FromCentimeter(widthCm - 6 - (hasLogo ? 1.6 : 0)));
        table.AddColumn(Unit.FromCentimeter(6));
        var row = table.AddRow();
        var at = 0;
        if (hasLogo)
        {
            var logo = row.Cells[at++].AddImage("base64:" + Convert.ToBase64String(head.Logo!));
            logo.Width = Unit.FromCentimeter(1.2);
            logo.LockAspectRatio = true;
        }

        var company = row.Cells[at++];
        Text(company.AddParagraph(head.CompanyName), 14, bold: true);
        var contact = string.Join(" · ", new[] { head.Website, head.Address }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (contact.Length > 0)
        {
            Text(company.AddParagraph(contact), 8.5, color: Muted);
        }

        var right = row.Cells[at];
        right.Format.Alignment = ParagraphAlignment.Right;
        Text(right.AddParagraph(kind.ToUpperInvariant()), 8, bold: true, color: Muted);
        Text(right.AddParagraph(number), 12, bold: true);
        if (stamp is not null)
        {
            Text(right.AddParagraph(stamp), 11, bold: true, color: Alert);
        }

        var rule = section.AddParagraph();
        rule.Format.SpaceBefore = Unit.FromPoint(10);
        rule.Format.Borders.Bottom.Color = Line;
        rule.Format.Borders.Bottom.Width = 0.75;
    }

    /// <summary>Side-by-side columns of labelled values, such as who is billed and the dates.</summary>
    public static void AddFacts(Section section, params IReadOnlyList<Fact>[] columns)
    {
        var table = section.AddTable();
        table.TopPadding = Unit.FromPoint(12);
        foreach (var _ in columns)
        {
            table.AddColumn(Unit.FromCentimeter(ContentWidthCm / columns.Length));
        }

        var row = table.AddRow();
        for (var i = 0; i < columns.Length; i++)
        {
            var first = true;
            foreach (var fact in columns[i])
            {
                var label = Text(row.Cells[i].AddParagraph(fact.Label.ToUpperInvariant()), 7.5, bold: true, color: Muted);
                label.Format.SpaceBefore = Unit.FromPoint(first ? 0 : 6);
                Text(row.Cells[i].AddParagraph(fact.Value), 10, bold: true);
                if (!string.IsNullOrWhiteSpace(fact.Note))
                {
                    Text(row.Cells[i].AddParagraph(fact.Note), 8.5, color: Muted);
                }

                first = false;
            }
        }
    }

    /// <summary>Labelled values in rows of <paramref name="perRow"/>, such as a report's summary figures.</summary>
    public static void AddFactGrid(Section section, IReadOnlyList<Fact> facts, int perRow = 4)
    {
        for (var at = 0; at < facts.Count; at += perRow)
        {
            var row = facts.Skip(at).Take(perRow).Select(f => (IReadOnlyList<Fact>)[f]).ToList();
            while (row.Count < perRow)
            {
                row.Add([]);
            }

            AddFacts(section, [.. row]);
        }
    }

    /// <summary>A large title under the letterhead, with a grey line under it.</summary>
    public static void AddTitle(Section section, string title, string subtitle)
    {
        var heading = Text(section.AddParagraph(title), 16, bold: true);
        heading.Format.SpaceBefore = Unit.FromPoint(12);
        Text(section.AddParagraph(subtitle), 9, color: Muted);
    }

    /// <summary>A short opening note to the client, in grey, between the title and the content.</summary>
    public static void AddIntro(Section section, string text)
    {
        var intro = Text(section.AddParagraph(text), 9.5, color: Muted);
        intro.Format.SpaceBefore = Unit.FromPoint(10);
        intro.Format.LineSpacingRule = LineSpacingRule.Multiple;
        intro.Format.LineSpacing = 1.25;
    }

    public static void AddHeading(Section section, string text)
    {
        var heading = Text(section.AddParagraph(text.ToUpperInvariant()), 8, bold: true, color: Muted);
        heading.Format.SpaceBefore = Unit.FromPoint(18);
        heading.Format.SpaceAfter = Unit.FromPoint(6);
        heading.Format.KeepWithNext = true;
    }

    /// <summary>A table whose header row repeats on every page it runs onto.</summary>
    public static Table AddTable(Section section, params TableColumn[] columns)
    {
        var table = section.AddTable();
        table.TopPadding = Unit.FromPoint(4);
        table.BottomPadding = Unit.FromPoint(4);
        table.Borders.Bottom.Color = Line;
        table.Borders.Bottom.Width = 0.5;
        foreach (var column in columns)
        {
            var added = table.AddColumn(Unit.FromCentimeter(column.WidthCm));
            added.Format.Alignment = column.AlignRight ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Shade;
        for (var i = 0; i < columns.Length; i++)
        {
            Text(header.Cells[i].AddParagraph(columns[i].Header), 8, bold: true, color: Muted);
        }

        return table;
    }

    /// <summary>One row; a null cell is left empty.</summary>
    public static Row AddRow(Table table, bool strong, params string?[] cells)
    {
        var row = table.AddRow();
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is { } text)
            {
                Text(row.Cells[i].AddParagraph(text), 9.5, bold: strong);
            }
        }

        return row;
    }

    /// <summary>A row of bold text across the whole table, naming the group of rows under it. It stays with the next row.</summary>
    public static void AddGroupRow(Table table, string label)
    {
        var row = table.AddRow();
        row.KeepWith = 1;
        row.Cells[0].MergeRight = table.Columns.Count - 1;
        Text(row.Cells[0].AddParagraph(label), 9.5, bold: true);
    }

    /// <summary>A smaller grey line under a cell's text.</summary>
    public static void AddCellNote(Row row, int cell, string note)
        => Text(row.Cells[cell].AddParagraph(note), 8, color: Muted);

    /// <summary>A row whose first cells are merged into one, for lines such as "3 more types, not listed".</summary>
    public static void AddSpanningRow(Table table, int merged, string label, string value)
    {
        var row = table.AddRow();
        row.Cells[0].MergeRight = merged - 1;
        Text(row.Cells[0].AddParagraph(label), 9, color: Muted);
        Text(row.Cells[merged].AddParagraph(value), 9.5);
    }

    /// <summary>A narrow column of totals on the right.</summary>
    public static void AddTotals(Section section, params TotalRow[] rows)
    {
        var table = section.AddTable();
        table.TopPadding = Unit.FromPoint(3);
        table.BottomPadding = Unit.FromPoint(3);
        table.Rows.LeftIndent = Unit.FromCentimeter(ContentWidthCm - 8);
        table.AddColumn(Unit.FromCentimeter(4.5));
        table.AddColumn(Unit.FromCentimeter(3.5)).Format.Alignment = ParagraphAlignment.Right;
        foreach (var total in rows)
        {
            var row = table.AddRow();
            if (total.Strong)
            {
                row.Borders.Top.Color = Ink;
                row.Borders.Top.Width = 0.75;
            }

            Text(row.Cells[0].AddParagraph(total.Label), total.Strong ? 10 : 9, bold: total.Strong, color: total.Strong ? Ink : Muted);
            Text(row.Cells[1].AddParagraph(total.Value), total.Strong ? 10.5 : 9.5, bold: total.Strong);
        }

        // The totals stay together on one page.
        table.Rows[0].KeepWith = rows.Length - 1;
    }

    /// <summary>A paragraph of small grey text, such as a note under a table.</summary>
    public static void AddNote(Section section, string text, double spaceBeforePt = 8)
    {
        var note = Text(section.AddParagraph(text), 8.5, color: Muted);
        note.Format.SpaceBefore = Unit.FromPoint(spaceBeforePt);
    }

    public static void AddParagraph(Section section, string text) => Text(section.AddParagraph(text), 9.5);

    public static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>$1,250.00, or 1,500,000 LBP.</summary>
    public static string Money(decimal amount, string currency = "USD")
    {
        var value = Math.Abs(amount);
        var body = currency switch
        {
            "USD" => "$" + value.ToString("N2", Invariant),
            "LBP" => value.ToString("N0", Invariant) + " LBP",
            _ => value.ToString("N2", Invariant) + " " + currency,
        };
        return amount < 0 ? "-" + body : body;
    }

    /// <summary>Hours and minutes as people say them: 8h, 7h 30m, 45m.</summary>
    public static string Duration(int minutes)
    {
        var (hours, rest) = (minutes / 60, minutes % 60);
        if (hours == 0)
        {
            return $"{rest}m";
        }

        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    /// <summary>A time of day from its minutes after midnight: 17:00, or 5:00 PM (24:00 is the end of the day).</summary>
    public static string TimeOfDay(int minute, bool use24Hour)
    {
        var (hour, rest) = (minute / 60, minute % 60);
        if (use24Hour)
        {
            return $"{hour}:{rest:00}";
        }

        return $"{(hour % 12 == 0 ? 12 : hour % 12)}:{rest:00} {(hour % 24 < 12 ? "AM" : "PM")}";
    }

    /// <summary>Sep 30, 2026.</summary>
    public static string Date(DateOnly day) => day.ToString("MMM d, yyyy", Invariant);

    private static Paragraph Text(Paragraph paragraph, double size, bool bold = false, Color? color = null)
    {
        paragraph.Format.Font.Size = size;
        paragraph.Format.Font.Bold = bold;
        if (color is { } c)
        {
            paragraph.Format.Font.Color = c;
        }

        return paragraph;
    }
}
