using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Documents.Implementation;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class DocumentPdfTests
{
    private static readonly Letterhead Head = new("Hourline Studio", "Beirut, Lebanon", "hourline.test", null);

    private static int Pages(byte[] pdf)
    {
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        using var stream = new MemoryStream(pdf);
        return PdfReader.Open(stream, PdfDocumentOpenMode.Import).PageCount;
    }

    private static InvoiceResponse Invoice(int lines) => new()
    {
        Id = 7,
        Number = "HL-2026-014",
        Project = new InvoiceProjectResponse(3, "Villa Haddad"),
        Client = new InvoiceClientResponse(2, "Haddad Holdings"),
        Currency = "USD",
        Total = lines * 100m,
        Paid = 50m,
        Balance = lines * 100m - 50m,
        DueOn = new DateOnly(2026, 10, 30),
        TermsDays = 30,
        Notes = "PO 44-1182",
        Lines = Enumerable.Range(1, lines).Select(i => new InvoiceLineResponse(i, i, $"Stage {i}", 100m)).ToList(),
    };

    [Fact]
    public void An_invoice_fits_one_page_and_a_long_one_runs_onto_more()
    {
        var billing = new BillingSettings { BankAccount = "Hourline SAL", BankName = "Bank Audi", Iban = "LB00 0000", Footer = "Thank you." };

        Pages(InvoicePdf.Render(Invoice(3), new DateOnly(2026, 9, 30), Head, billing)).ShouldBe(1);
        Pages(InvoicePdf.Render(Invoice(80), new DateOnly(2026, 9, 30), Head, billing)).ShouldBeGreaterThan(1);
        Pages(InvoicePdf.Render(Invoice(1) with { Number = null, DueOn = null }, null, Head, null)).ShouldBe(1);
    }

    [Theory]
    [InlineData(StatementMode.Detail, false)]
    [InlineData(StatementMode.Detail, true)]
    [InlineData(StatementMode.Total, false)]
    public void A_statement_renders_in_every_mode(StatementMode mode, bool includesEarlier)
    {
        var statement = new StatementResponse
        {
            Id = 1,
            Number = "ST-2026-003",
            ProjectTitle = "Site 12",
            Client = new ProjectClientResponse(2, "Haddad Holdings"),
            Mode = mode,
            IncludesEarlier = includesEarlier,
            Date = new DateOnly(2026, 9, 30),
            Types = mode == StatementMode.Total ? [] : [new StatementTypeResponse("cement", "Cement", "40 bag", 2, includesEarlier ? 110m : 0, 55m, 165m)],
            HiddenTypes = includesEarlier ? new StatementHiddenTypesResponse(1, 220m) : null,
            Total = 385m,
            Count = 3,
            Received = 500m,
            Remaining = 1930m,
        };

        Pages(StatementPdf.Render(statement, Head)).ShouldBe(1);
    }

    [Fact]
    public void Money_and_dates_are_written_in_full()
    {
        PdfLayout.Money(1250m).ShouldBe("$1,250.00");
        PdfLayout.Money(-5.5m).ShouldBe("-$5.50");
        PdfLayout.Money(1500000m, "LBP").ShouldBe("1,500,000 LBP");
        PdfLayout.Date(new DateOnly(2026, 9, 3)).ShouldBe("Sep 3, 2026");
    }

    [Fact]
    public void A_project_report_renders_every_section()
    {
        var project = new ProjectResponse
        {
            Id = 12,
            Title = "Villa Haddad",
            Description = "Interior design of a two-floor villa.",
            Client = new ProjectClientResponse(2, "Haddad Holdings"),
            Owner = new ProjectOwnerResponse(1, "Nadia Khoury"),
            StartDate = new DateOnly(2026, 6, 1),
            ProgressPct = 40,
        };
        var contributor = new ProjectContributorResponse(
            5, "Rami Aoun", 3, 540, 60, 100, new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc), [], null);
        var work = new ProjectWorkResponse(
            12, 540, 60, 3, 1, null, default, 0, 0, [], [], [contributor]);
        var report = new ProjectReport(
            ProjectReportKind.Internal,
            Enum.GetValues<ProjectReportSection>().ToHashSet(),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 30),
            null,
            project,
            work,
            null,
            null,
            [],
            [],
            null,
            [new(1, 5, "Rami Aoun", "Interiors", new DateTime(2026, 9, 29, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc), 180, "Kitchen layout")],
            3,
            utc => utc.AddHours(3));

        Pages(ProjectReportPdf.Render(report, Head)).ShouldBe(1);
    }
}
