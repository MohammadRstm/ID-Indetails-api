using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Documents.Implementation;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class DocumentPdfTests
{
    private static readonly Letterhead Head = new("ID InDetails", "Beirut, Lebanon", "id-indetails.test", null);

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
        var billing = new BillingSettings { BankAccount = "ID InDetails SAL", BankName = "Bank Audi", Iban = "LB00 0000", Footer = "Thank you." };

        Pages(InvoicePdf.Render(Invoice(3), new DateOnly(2026, 9, 30), Head, billing)).ShouldBe(1);
        Pages(InvoicePdf.Render(Invoice(80), new DateOnly(2026, 9, 30), Head, billing)).ShouldBeGreaterThan(1);
        Pages(InvoicePdf.Render(Invoice(1) with { Number = null, DueOn = null }, null, Head, null)).ShouldBe(1);
    }

    private static BoqDocument Boq(BoqKind kind, BoqStatus status, int lines, BoqAdjustmentType adjustment = BoqAdjustmentType.None, decimal? value = null)
    {
        var items = Enumerable.Range(1, lines)
            .Select(i => new BoqLineResponse(i, i, $"2.1.{i}", $"Reinforced concrete slab, level {i}", 10m, 0.05m, 10.5m, "m3", 120.5m, 1265.25m))
            .ToList();
        var subtotal = items.Sum(l => l.TotalUsd);
        var total = adjustment == BoqAdjustmentType.Percent ? subtotal + Math.Round(subtotal * (value ?? 0), 2) : adjustment == BoqAdjustmentType.Total ? value!.Value : subtotal;
        var boq = new BoqDetailResponse(
            9, kind, kind == BoqKind.Vo ? "VO-2026-002" : "BQ-2026-001", "Villa Mounir", new PersonRefResponse(1, "Nadia Khoury"), status, total, lines,
            kind == BoqKind.Vo ? "The client added a guest house" : null, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            null, null, null, 4, adjustment, value, items, subtotal, total - subtotal, 0.10m, total * 0.10m, 0.11m, total * 0.11m, total * 1.21m, []);

        return new BoqDocument(boq, "Site 12", "Beit Mery", "Haddad Holdings");
    }

    [Theory]
    [InlineData(BoqKind.Client, BoqStatus.Draft, BoqAdjustmentType.None)]
    [InlineData(BoqKind.Client, BoqStatus.Agreed, BoqAdjustmentType.Percent)]
    [InlineData(BoqKind.Vo, BoqStatus.Draft, BoqAdjustmentType.Total)]
    public void A_boq_renders_with_or_without_an_adjustment_and_a_long_one_runs_onto_more_pages(BoqKind kind, BoqStatus status, BoqAdjustmentType adjustment)
    {
        decimal? value = adjustment == BoqAdjustmentType.Percent ? -0.05m : adjustment == BoqAdjustmentType.Total ? 900m : null;

        Pages(BoqPdf.Render(Boq(kind, status, 3, adjustment, value), new DateOnly(2026, 9, 30), Head)).ShouldBe(1);
        Pages(BoqPdf.Render(Boq(kind, status, 120, adjustment, value), new DateOnly(2026, 9, 30), Head)).ShouldBeGreaterThan(1);
    }

    [Fact]
    public void A_boq_shows_the_totals_the_client_pays_and_never_the_exact_quantity_or_waste()
    {
        var boq = Boq(BoqKind.Client, BoqStatus.Agreed, 2, BoqAdjustmentType.Percent, -0.05m).Boq;

        BoqPdf.Totals(boq).Select(t => t.Label).ShouldBe(["Subtotal", "Adjustment (-5%)", "Total", "Supervision (10%)", "TVA (11%)", "Total to pay"]);
        BoqPdf.Totals(Boq(BoqKind.Client, BoqStatus.Draft, 2).Boq).Select(t => t.Label).ShouldBe(["Total", "Supervision (10%)", "TVA (11%)", "Total to pay"]);
        BoqPdf.Totals(boq).Last().Value.ShouldBe(PdfLayout.Money(boq.ClientTotalUsd!.Value));

        // the figures the client reads: the final quantity, never the quantity before waste
        BoqPdf.Quantity(boq.Lines[0].FinalQuantity).ShouldBe("10.5");
        BoqPdf.Quantity(1234.5m).ShouldBe("1,234.5");
        BoqPdf.Rate(120.5m).ShouldBe("$120.50");
        BoqPdf.Rate(0.0125m).ShouldBe("$0.0125");
        BoqPdf.Rate(-250m).ShouldBe("-$250.00");
        BoqPdf.Percent(0.11m).ShouldBe("11%");
        BoqPdf.Percent(0.125m).ShouldBe("12.5%");
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
