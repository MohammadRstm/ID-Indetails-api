using System.Globalization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>A month of salaries as the Salaries page and its CSV list them, and the filters that narrowed them.</summary>
/// <param name="Salaries">The whole filtered list (not one page), as <c>SalaryService</c> works it out.</param>
public sealed record SalariesReport(SalariesResponse Salaries, string? Domain, string? Role, string? Search, DateOnly Today);

/// <summary>
/// The salaries of a month on a landscape page: one row per person, then a total per currency and the people with no
/// pay set. Internal: it carries pay, so it has no watermark and says so in its footer.
/// </summary>
public static class SalariesPdf
{
    public static byte[] Render(SalariesReport report, Letterhead head)
    {
        var salaries = report.Salaries;
        var month = salaries.Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var footer = $"{head.CompanyName} · Internal: contains pay information";
        var (document, section) = PdfLayout.NewDocument($"Salaries · {month}", footer, landscape: true);
        PdfLayout.AddLetterhead(section, head, "Salaries", "Generated " + PdfLayout.Date(report.Today), widthCm: PdfLayout.LandscapeContentWidthCm);

        var subtitle = new List<string> { salaries.InProgress ? "In progress, to " + PdfLayout.Date(report.Today) : "Closed month" };
        subtitle.AddRange(Filters(report));
        PdfLayout.AddTitle(section, $"Salaries · {month}", string.Join(" · ", subtitle));

        var table = PdfLayout.AddTable(
            section,
            new TableColumn("Person", 5.0),
            new TableColumn("Pay basis", 3.0),
            new TableColumn("Expected", 1.8, AlignRight: true),
            new TableColumn("Logged", 2.4, AlignRight: true),
            new TableColumn("Overtime paid", 1.9, AlignRight: true),
            new TableColumn("Short", 1.8, AlignRight: true),
            new TableColumn("Base pay", 2.6, AlignRight: true),
            new TableColumn("Deduction", 2.4, AlignRight: true),
            new TableColumn("Overtime pay", 2.4, AlignRight: true),
            new TableColumn("To pay", 2.8, AlignRight: true));

        foreach (var r in salaries.Rows.Items)
        {
            var row = PdfLayout.AddRow(
                table,
                false,
                r.Name,
                r.Basis == PayBasis.Monthly ? "Monthly" : "Hourly",
                r.ExpectedMinutes is { } expected ? PdfLayout.Duration(expected) : "-",
                r.TracksHours ? PdfLayout.Duration(r.NormalMinutes + r.MadeUpMinutes + r.OvertimeMinutes) : "Not tracked",
                r.TracksHours ? PdfLayout.Duration(r.OvertimePaidMinutes) : "-",
                r.ShortMinutes is > 0 ? PdfLayout.Duration(r.ShortMinutes.Value) : "-",
                PdfLayout.Money(r.BasePay, r.Currency),
                Deducted(r.Deduction, r.Currency),
                r.OvertimePay > 0 ? PdfLayout.Money(r.OvertimePay, r.Currency) : "-",
                PdfLayout.Money(r.Total, r.Currency));
            PdfLayout.AddCellNote(row, 0, r.Team?.Name ?? (r.Domain is { } domain ? Capitalised(domain) : "Company"));
            PdfLayout.AddCellNote(row, 1, PdfLayout.Money(r.Amount, r.Currency) + (r.Basis == PayBasis.Hourly ? " / h" : string.Empty));
        }

        foreach (var t in salaries.Totals)
        {
            PdfLayout.AddRow(
                table,
                true,
                $"Total · {t.Currency}",
                $"{t.People} {(t.People == 1 ? "person" : "people")}",
                null,
                null,
                null,
                null,
                PdfLayout.Money(t.BasePay, t.Currency),
                Deducted(t.Deduction, t.Currency),
                t.OvertimePay > 0 ? PdfLayout.Money(t.OvertimePay, t.Currency) : "-",
                PdfLayout.Money(t.Total, t.Currency));
        }

        if (salaries.Rows.Items.Count == 0)
        {
            PdfLayout.AddNote(section, "No one matches: nobody with pay set was employed in this month.");
        }

        if (salaries.WithoutPay.Count > 0)
        {
            PdfLayout.AddNote(section, "No pay set: " + string.Join(", ", salaries.WithoutPay.Select(p => p.Name)) + ".");
        }

        return PdfLayout.Render(document);
    }

    /// <summary>The business, role and search that narrowed the list, when any did.</summary>
    private static IEnumerable<string> Filters(SalariesReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.Domain))
        {
            yield return "Business: " + Capitalised(report.Domain);
        }

        if (!string.IsNullOrWhiteSpace(report.Role))
        {
            yield return "Role: " + RoleLabel(report.Role);
        }

        if (!string.IsNullOrWhiteSpace(report.Search))
        {
            yield return $"Search: \"{report.Search.Trim()}\"";
        }
    }

    /// <summary>A deduction is written as the minus it is; none is a dash.</summary>
    private static string Deducted(decimal deduction, string currency) => deduction > 0 ? PdfLayout.Money(-deduction, currency) : "-";

    private static string RoleLabel(string role) => role switch
    {
        RoleCodes.SuperAdmin => "Super admins",
        RoleCodes.Admin => "Admins",
        RoleCodes.Employee => "Employees",
        _ => role,
    };

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
