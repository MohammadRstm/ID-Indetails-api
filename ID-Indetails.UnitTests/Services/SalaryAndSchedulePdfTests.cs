using System.Text;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Documents.Implementation;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class SalaryAndSchedulePdfTests
{
    private static readonly Letterhead Head = new("ID InDetails", "Beirut, Lebanon", "id-indetails.test", null);
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static SalaryRowResponse Row(int i, bool tracksHours = true, decimal? advance = null) => new()
    {
        UserId = i,
        Name = $"Person {i}",
        Email = $"person{i}@test.local",
        Role = "employee",
        Domain = "design",
        Team = new TeamSummaryResponse(1, "Architecture"),
        Basis = i % 2 == 0 ? PayBasis.Monthly : PayBasis.Hourly,
        Amount = 1760m,
        Currency = "USD",
        HourlyRate = 10m,
        OvertimeRate = 9.5m,
        TracksHours = tracksHours,
        ExpectedMinutes = tracksHours ? 9600 : null,
        NormalMinutes = 9000,
        MadeUpMinutes = 60,
        OvertimeMinutes = 120,
        OvertimePaidMinutes = 90,
        ExcusedMinutes = 0,
        ShortMinutes = 540,
        Flagged = false,
        Behind = false,
        ProRated = false,
        WorkingDays = 20,
        WorkingDaysPaid = 20,
        Deduction = 90m,
        BasePay = 1760m,
        OvertimePay = 22.5m,
        Total = 1692.5m,
        MonthlyPay = 1760m,
        EarnedSoFar = 1692.5m,
        AdvanceRepayment = advance,
        ToHandOver = 1692.5m - (advance ?? 0),
    };

    private static SalariesResponse Salaries(IReadOnlyList<SalaryRowResponse> rows, bool inProgress = true) => new(
        new DateOnly(2026, 11, 1),
        inProgress,
        1.5m,
        new PagedResponse<SalaryRowResponse>(rows, 1, rows.Count, rows.Count),
        [new SalaryTotalResponse("USD", rows.Count, rows.Sum(r => r.BasePay), rows.Sum(r => r.OvertimePay), rows.Sum(r => r.Deduction), rows.Sum(r => r.Total), rows.Sum(r => r.EarnedSoFar), rows.Sum(r => r.AdvanceRepayment ?? 0), rows.Sum(r => r.ToHandOver))],
        new SalarySummaryResponse(rows.Count, rows.Count, 0, 0, 0, 0, 0, 0, null),
        [new PersonWithoutPayResponse(99, "Unpaid Person", "employee", "design")],
        new SalaryAdvanceSummaryResponse(0, [], 0));

    private static PersonScheduleResponse Person(string name, string? domain, string? team, bool own = false, DateOnly? next = null, string? position = null) => new(
        name.Length,
        name,
        position,
        "employee",
        domain,
        team is null ? null : new TeamSummaryResponse(1, team),
        own,
        null,
        [new ScheduleDayResponse(1, 540, 1020, 420), new ScheduleDayResponse(2, 540, 1020, 420)],
        840,
        next,
        null,
        null);

    private static SchedulesResponse Week(params PersonScheduleResponse[] people) => new(
        new WorkingTimePolicyResponse(1, new DateOnly(2026, 1, 1), [1, 2, 3, 4, 5], 540, 1020, 60, 1.5m, 420, 2100, 184),
        people);

    private static (int Pages, bool Landscape, int Pictures) Inspect(byte[] pdf)
    {
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        using var stream = new MemoryStream(pdf);
        var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = document.Pages[0];
        // PDFsharp keeps a landscape page's box upright and turns it a quarter with /Rotate, so read it as it is shown
        var box = page.MediaBox;
        var landscape = page.Rotate % 180 == 0 ? box.Width > box.Height : box.Height > box.Width;
        return (document.PageCount, landscape, page.Resources.Elements.GetDictionary("/XObject")?.Elements.Count ?? 0);
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(45, "45m")]
    [InlineData(480, "8h")]
    [InlineData(450, "7h 30m")]
    public void Durations_read_as_people_say_them(int minutes, string expected) => PdfLayout.Duration(minutes).ShouldBe(expected);

    [Theory]
    [InlineData(540, true, "9:00")]
    [InlineData(1020, true, "17:00")]
    [InlineData(1020, false, "5:00 PM")]
    [InlineData(540, false, "9:00 AM")]
    [InlineData(0, false, "12:00 AM")]
    [InlineData(720, false, "12:00 PM")]
    [InlineData(1440, true, "24:00")]
    public void A_time_of_day_follows_the_companys_clock(int minute, bool use24Hour, string expected)
        => PdfLayout.TimeOfDay(minute, use24Hour).ShouldBe(expected);

    [Fact]
    public void The_salaries_pdf_is_a_landscape_page_with_no_watermark()
    {
        var report = new SalariesReport(Salaries([Row(1), Row(2), Row(3, tracksHours: false)]), "design", "employee", "per", Today);

        var (pages, landscape, pictures) = Inspect(SalariesPdf.Render(report, Head));

        pages.ShouldBe(1);
        landscape.ShouldBeTrue();
        pictures.ShouldBe(0);
    }

    [Fact]
    public void The_salaries_pdf_adds_the_advance_columns_only_when_someone_has_an_advance()
    {
        var plain = SalariesPdf.Render(new SalariesReport(Salaries([Row(1), Row(2)]), null, null, null, Today), Head);
        var withAdvance = SalariesPdf.Render(new SalariesReport(Salaries([Row(1, advance: 300m), Row(2)]), null, null, null, Today), Head);

        Inspect(withAdvance).Pages.ShouldBe(1);
        Inspect(withAdvance).Landscape.ShouldBeTrue();
        withAdvance.Length.ShouldBeGreaterThan(plain.Length);
    }

    [Fact]
    public void A_long_salaries_list_runs_onto_more_pages_and_a_closed_month_with_nobody_still_renders()
    {
        var many = SalariesPdf.Render(new SalariesReport(Salaries(Enumerable.Range(1, 80).Select(i => Row(i)).ToList()), null, null, null, Today), Head);
        Inspect(many).Pages.ShouldBeGreaterThan(1);

        var none = SalariesPdf.Render(new SalariesReport(Salaries([], inProgress: false), null, null, null, Today), Head);
        Inspect(none).Pages.ShouldBe(1);
    }

    [Fact]
    public void The_weeks_columns_start_on_the_companys_week_start()
    {
        SchedulesPdf.WeekOrder(1).ShouldBe([1, 2, 3, 4, 5, 6, 0]);
        SchedulesPdf.WeekOrder(0).ShouldBe([0, 1, 2, 3, 4, 5, 6]);
        SchedulesPdf.WeekOrder(6).ShouldBe([6, 0, 1, 2, 3, 4, 5]);
    }

    [Fact]
    public void People_are_grouped_by_business_then_department_with_super_admins_last()
    {
        var people = new[]
        {
            Person("Zoe", "execution", null),
            Person("Root", null, null),
            Person("Nadim", "design", "Visualisation"),
            Person("Maya", "design", null),
            Person("Alex", "design", "Architecture"),
            Person("Amal", "design", "Architecture"),
            Person("Karim", "execution", "Site"),
        };

        var both = SchedulesPdf.Groups(people, bothBusinesses: true);
        both.Select(g => g.Name).ShouldBe(["Architecture", "Visualisation", "Design · no department", "Site", "Execution · no department", "Company"]);
        both[0].People.Select(p => p.Name).ShouldBe(["Alex", "Amal"]);

        SchedulesPdf.Groups([Person("Maya", "design", null)], bothBusinesses: false).Single().Name.ShouldBe("No department");
    }

    [Fact]
    public void The_schedules_pdf_is_a_landscape_page_with_no_watermark_in_either_clock()
    {
        var people = Week(Person("Alex", "design", "Architecture", own: true, next: new DateOnly(2026, 12, 1), position: "Architect"), Person("Root", null, null));

        foreach (var use24Hour in new[] { true, false })
        {
            var pdf = SchedulesPdf.Render(new SchedulesReport(people, Today, 1, use24Hour, true, "design", "al"), Head);
            var (pages, landscape, pictures) = Inspect(pdf);
            (pages, landscape, pictures).ShouldBe((1, true, 0));
        }
    }

    [Fact]
    public void The_schedules_csv_has_a_row_per_person_in_the_pdfs_order_and_is_safe_in_a_spreadsheet()
    {
        var people = Week(
            Person("=HYPERLINK(\"x\")", "execution", null, position: "Foreman, site"),
            Person("Alex", "design", "Architecture", own: true, next: new DateOnly(2026, 12, 1)));

        var bytes = SchedulesCsv.Write(new SchedulesReport(people, Today, 1, true, true, null, null));

        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Name,Business,Department,Position,Schedule,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday,Hours a week,Next change");
        lines[1].ShouldBe("Alex,Design,Architecture,,Own,09:00-17:00,09:00-17:00,Off,Off,Off,Off,Off,14,2026-12-01");
        lines[2].ShouldBe("\"'=HYPERLINK(\"\"x\"\")\",Execution,,\"Foreman, site\",Default,09:00-17:00,09:00-17:00,Off,Off,Off,Off,Off,14,");
        lines.Length.ShouldBe(3);
    }
}
