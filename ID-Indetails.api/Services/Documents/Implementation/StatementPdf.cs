using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Enums;
using MigraDoc.DocumentObjectModel.Tables;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>A client statement of the materials supplied on a site. It is not an invoice and says so.</summary>
public static class StatementPdf
{
    public static byte[] Render(StatementResponse statement, Letterhead head)
    {
        var money = (decimal amount) => PdfLayout.Money(amount, statement.Currency);
        var (document, section) = PdfLayout.NewDocument(
            $"Materials statement {statement.Number}",
            $"{head.CompanyName} · Materials statement {statement.Number}");
        PdfLayout.AddLetterhead(section, head, "Materials statement", statement.Number, statement.IsPreview ? "PREVIEW" : null);
        PdfLayout.AddFacts(
            section,
            [new Fact("Prepared for", statement.Client.DisplayName)],
            [new Fact("Project", statement.ProjectTitle, statement.ProjectLocation)],
            [new Fact("Date", PdfLayout.Date(statement.Date))]);

        PdfLayout.AddHeading(section, "Material type");
        var split = statement.Mode == StatementMode.Detail && statement.IncludesEarlier;
        if (statement.Mode == StatementMode.Total)
        {
            var table = PdfLayout.AddTable(section, new TableColumn("Description", 13.4), new TableColumn("Amount", 4, AlignRight: true));
            var since = statement.Since is { } day ? " since " + PdfLayout.Date(day) : " to date";
            var row = PdfLayout.AddRow(table, false, "Materials supplied" + since, money(statement.Total));
            PdfLayout.AddCellNote(row, 0, statement.Count == 1 ? "1 item" : $"{statement.Count} items");
        }
        else if (split)
        {
            var table = PdfLayout.AddTable(
                section,
                new TableColumn("Material", 6),
                new TableColumn("Quantity", 2.6),
                new TableColumn("On earlier statements", 3.1, AlignRight: true),
                new TableColumn("New this time", 2.8, AlignRight: true),
                new TableColumn("Total", 2.9, AlignRight: true));
            foreach (var type in statement.Types)
            {
                var row = PdfLayout.AddRow(
                    table, false, type.Label, type.Quantity ?? "-", Amount(type.Earlier, money), Amount(type.New, money), money(type.Total));
                Deliveries(row, type);
            }

            Hidden(table, 4, statement.HiddenTypes, money);
        }
        else
        {
            var table = PdfLayout.AddTable(
                section,
                new TableColumn("Material", 10.4),
                new TableColumn("Quantity", 3),
                new TableColumn("Amount", 4, AlignRight: true));
            foreach (var type in statement.Types)
            {
                Deliveries(PdfLayout.AddRow(table, false, type.Label, type.Quantity ?? "-", money(type.Total)), type);
            }

            Hidden(table, 2, statement.HiddenTypes, money);
        }

        var totals = new List<TotalRow>();
        if (split)
        {
            totals.Add(new("On earlier statements", money(statement.EarlierTotal)));
            totals.Add(new("New this time", money(statement.NewTotal)));
        }

        totals.Add(new("Materials total", money(statement.Total), Strong: true));
        PdfLayout.AddTotals(section, [.. totals]);

        PdfLayout.AddHeading(section, "Payment summary");
        PdfLayout.AddTotals(
            section,
            new TotalRow("Received from you so far", money(statement.Received)),
            statement.PaidAhead > 0
                ? new TotalRow("Paid in advance", money(statement.PaidAhead), Strong: true)
                : new TotalRow("Remaining amount", money(statement.Remaining), Strong: true));
        PdfLayout.AddNote(section, "The remaining amount is what is left of everything agreed on this project so far, after the payments above.");
        PdfLayout.AddNote(section, "This is a statement of materials for your information. It is not an invoice.", spaceBeforePt: 18);

        return PdfLayout.Render(document);
    }

    private static string Amount(decimal amount, Func<decimal, string> money) => amount == 0 ? "-" : money(amount);

    private static void Deliveries(Row row, StatementTypeResponse type)
    {
        if (type.Deliveries > 1)
        {
            PdfLayout.AddCellNote(row, 0, $"{type.Deliveries} deliveries");
        }
    }

    private static void Hidden(Table table, int merged, StatementHiddenTypesResponse? hidden, Func<decimal, string> money)
    {
        if (hidden is { Count: > 0 })
        {
            var types = hidden.Count == 1 ? "1 more material type" : $"{hidden.Count} more material types";
            PdfLayout.AddSpanningRow(table, merged, $"{types} from earlier statements, not listed again", money(hidden.Total));
        }
    }
}
