using System.Globalization;
using System.Text;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Overtime across the Design views, from one designer's June 2026 under the seeded 9–5 week (7h required, a paid 1h
/// break). Beirut is UTC+3 then, so 06:00 UTC is 09:00:
/// <list type="bullet">
/// <item>Monday 15 June, 09:00–16:00 on the first project: 7h within hours, no overtime.</item>
/// <item>Monday 15 June, 16:00–18:00 on the second: 1h after 5 PM and 1h past the day's 7h, so 2h of overtime.</item>
/// <item>Saturday 20 June, 09:00–11:00 on the first: a day off, so 2h of overtime.</item>
/// </list>
/// </summary>
public class OvertimeViewsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task Every_listed_entry_carries_its_share_of_the_day_s_overtime()
    {
        var (superAdmin, designer, _, _) = await JuneAsync();

        var theirs = await OkAsync<PagedResponse<WorkEntryResponse>>(
            $"/api/work-entries?userId={designer.User.Id}&from=2026-06-01&to=2026-06-30", superAdmin);
        OvertimeOf(theirs.Items).ShouldBe([("Floor plans", 0), ("Late render", 120), ("Weekend renders", 120)]);

        var mine = await OkAsync<PagedResponse<WorkEntryResponse>>("/api/me/work-entries?from=2026-06-01&to=2026-06-30", designer);
        OvertimeOf(mine.Items).ShouldBe(OvertimeOf(theirs.Items));

        // Alone, 16:00–18:00 would only be 1h of overtime: the morning's 7h are what make the rest of it overtime.
        var late = theirs.Items.Single(e => e.Description == "Late render");
        (await OkAsync<WorkEntryDetailResponse>($"/api/work-entries/{late.Id}", designer)).Entry.OvertimeMinutes.ShouldBe(120);
    }

    [Fact]
    public async Task The_export_has_an_overtime_column()
    {
        var (superAdmin, designer, _, _) = await JuneAsync();

        var response = await Client.GetAsync($"/api/work-entries/export?userId={designer.User.Id}&from=2026-06-01&to=2026-06-30", superAdmin.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldStartWith("Date,Weekday,Start,End,Hours,Overtime hours,Employee,");
        lines.Single(l => l.Contains("Floor plans")).ShouldContain(",7.00,0.00,");
        lines.Single(l => l.Contains("Late render")).ShouldContain(",2.00,2.00,");
        lines.Single(l => l.Contains("Weekend renders")).ShouldContain(",2.00,2.00,");
    }

    [Fact]
    public async Task My_work_splits_normal_time_and_overtime()
    {
        var (_, designer, first, _) = await JuneAsync();

        var summary = await OkAsync<WorkSummaryResponse>("/api/me/work-summary", designer);
        summary.ShouldSatisfyAllConditions(
            s => s.TotalMinutes.ShouldBe(660),
            s => s.TotalOvertimeMinutes.ShouldBe(240));
        summary.ByProject.Single(p => p.ProjectId == first.Id).ShouldSatisfyAllConditions(
            p => p.Minutes.ShouldBe(540),
            p => p.OvertimeMinutes.ShouldBe(120));

        var day = await OkAsync<MyDayResponse>("/api/me/day?date=2026-06-15", designer);
        day.LoggedMinutes.ShouldBe(540);
        day.OvertimeMinutes.ShouldBe(120);

        var project = await OkAsync<MyProjectWorkResponse>($"/api/me/projects/{first.Id}/work", designer);
        project.MyMinutes.ShouldBe(540);
        project.MyOvertimeMinutes.ShouldBe(120);
        OvertimeOf(project.Recent).ShouldBe([("Floor plans", 0), ("Weekend renders", 120)]);
    }

    [Fact]
    public async Task A_person_s_summary_splits_their_overtime_and_outlasts_their_account()
    {
        var (superAdmin, designer, _, second) = await JuneAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var theirs = await OkAsync<WorkSummaryResponse>($"/api/employees/{designer.User.Id}/work-summary?projectId={second.Id}", designAdmin);
        theirs.TotalOvertimeMinutes.ShouldBe(240);
        theirs.ByProject.ShouldHaveSingleItem().OvertimeMinutes.ShouldBe(120);

        await (await Client.GetAsync($"/api/employees/{siteWorker.User.Id}/work-summary", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/employees/{designer.User.Id}/work-summary", siteWorker.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // Someone who left keeps their history. Their access token still signs them in until it expires, but they hold no
        // permissions any more, so their own pages refuse them.
        (await Client.PostEmptyAsync($"/api/employees/{designer.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<WorkSummaryResponse>($"/api/employees/{designer.User.Id}/work-summary", designAdmin)).TotalOvertimeMinutes.ShouldBe(240);
        await (await Client.GetAsync("/api/me/work-summary", designer.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Project_work_splits_each_contributor_s_hours_and_filters_them_by_work_text()
    {
        var (superAdmin, designer, first, _) = await JuneAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        var work = await OkAsync<ProjectWorkResponse>($"/api/projects/{first.Id}/work", designAdmin);
        work.TotalMinutes.ShouldBe(540);
        work.TotalOvertimeMinutes.ShouldBe(120);
        work.Contributors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            c => c.Minutes.ShouldBe(540),
            c => c.OvertimeMinutes.ShouldBe(120));

        // The work text narrows the contributors (every word, in the description), never the project's totals.
        var weekend = await OkAsync<ProjectWorkResponse>($"/api/projects/{first.Id}/work?search=RENDERS%20weekend", designAdmin);
        weekend.TotalMinutes.ShouldBe(540);
        weekend.Contributors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            c => c.Entries.ShouldBe(1),
            c => c.Minutes.ShouldBe(120),
            c => c.OvertimeMinutes.ShouldBe(120));
        (await OkAsync<ProjectWorkResponse>($"/api/projects/{first.Id}/work?search=elevations", designAdmin)).Contributors.ShouldBeEmpty();

        // The contributor drawer takes the same filters; its share is of all the project's time (420 of 540).
        var drawer = await OkAsync<ProjectContributorDetailResponse>(
            $"/api/projects/{first.Id}/work/people/{designer.User.Id}?search=floor&to=2026-06-19", designAdmin);
        drawer.ShouldSatisfyAllConditions(
            d => d.Minutes.ShouldBe(420),
            d => d.OvertimeMinutes.ShouldBe(0),
            d => d.SharePct.ShouldBe(78),
            d => d.RecentEntries.ShouldHaveSingleItem().Description.ShouldBe("Floor plans"));
        (await OkAsync<ProjectContributorDetailResponse>($"/api/projects/{first.Id}/work/people/{designer.User.Id}", designAdmin))
            .OvertimeMinutes.ShouldBe(120);

        await (await Client.GetAsync($"/api/projects/{first.Id}/work?search={new string('a', 101)}", designAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync($"/api/projects/{site.Id}/work?search=floor", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{first.Id}/work/people/{designer.User.Id}?search=floor", designer.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // Work of someone who has left still counts.
        (await Client.PostEmptyAsync($"/api/employees/{designer.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<ProjectWorkResponse>($"/api/projects/{first.Id}/work", designAdmin)).Contributors.ShouldHaveSingleItem().OvertimeMinutes.ShouldBe(120);
    }

    [Fact]
    public async Task The_work_report_splits_each_person_s_hours()
    {
        var (superAdmin, designer, _, second) = await JuneAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var range = "from=2026-06-01&to=2026-06-30";

        var report = await OkAsync<WorkReportResponse>($"/api/reports/work?{range}&userId={designer.User.Id}", designAdmin);
        report.ShouldSatisfyAllConditions(
            r => r.TotalMinutes.ShouldBe(660),
            r => r.OvertimeMinutes.ShouldBe(240),
            r => r.ByPerson.ShouldHaveSingleItem().OvertimeMinutes.ShouldBe(240),
            r => r.Matrix.Rows.ShouldHaveSingleItem().OvertimeMinutes.ShouldBe(240));

        // Narrowed to one project, its entries are still judged against the person's whole day.
        (await OkAsync<WorkReportResponse>($"/api/reports/work?{range}&projectId={second.Id}", designAdmin)).OvertimeMinutes.ShouldBe(120);

        (await Client.PostEmptyAsync($"/api/employees/{designer.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<WorkReportResponse>($"/api/reports/work?{range}&userId={designer.User.Id}", designAdmin)).OvertimeMinutes.ShouldBe(240);
    }

    [Fact]
    public async Task The_overview_counts_the_period_s_overtime()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var before = await OkAsync<OverviewResponse>("/api/reports/overview?period=last30", superAdmin);

        // Two hours on the latest Saturday, a day off, so all of it is overtime.
        var saturday = TodayInBeirut().AddDays(-1);
        while (saturday.DayOfWeek != DayOfWeek.Saturday)
        {
            saturday = saturday.AddDays(-1);
        }

        var start = TimeZoneInfo.ConvertTimeToUtc(saturday.ToDateTime(new TimeOnly(9, 0)), Beirut);
        await Work.InsertAsync(
            designer.User.Id,
            project.Id,
            start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            start.AddHours(2).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        var after = await OkAsync<OverviewResponse>("/api/reports/overview?period=last30", superAdmin);
        (after.Minutes - before.Minutes).ShouldBe(120);
        (after.OvertimeMinutes - before.OvertimeMinutes).ShouldBe(120);
        await (await Client.GetAsync("/api/reports/overview", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task The_schedule_shows_any_day_s_hours_for_the_logging_preview()
    {
        var (superAdmin, designer, _, _) = await JuneAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        await JoinedInJanuaryAsync(designer);

        var monday = (await OkAsync<MyScheduleResponse>("/api/me/schedule?date=2026-06-15", designer)).Day;
        monday.ShouldSatisfyAllConditions(
            d => d.Date.ShouldBe(new DateOnly(2026, 6, 15)),
            d => d.IsWorkingDay.ShouldBeTrue(),
            d => d.StartMinute.ShouldBe(540),
            d => d.EndMinute.ShouldBe(1020),
            d => d.RequiredMinutes.ShouldBe(420),
            d => d.PaidBreakMinutes.ShouldBe(60),
            d => d.LoggedMinutes.ShouldBe(540),
            d => d.InsideMinutes.ShouldBe(420),
            d => d.OvertimeMinutes.ShouldBe(120),
            d => d.LeftMinutes.ShouldBe(0),
            // June's 22 working days of 7h, less the 7h worked within hours and the 4h of overtime that made hours up.
            d => d.MonthShortMinutes.ShouldBe(22 * 420 - 420 - 240));

        var saturday = (await OkAsync<MyScheduleResponse>("/api/me/schedule?date=2026-06-20", designer)).Day;
        saturday.IsWorkingDay.ShouldBeFalse();
        saturday.OvertimeMinutes.ShouldBe(120);

        // Admins log hours without an hours ledger.
        (await OkAsync<MyScheduleResponse>("/api/me/schedule?date=2026-06-15", designAdmin)).Day.MonthShortMinutes.ShouldBeNull();

        await (await Client.GetAsync("/api/me/schedule?date=1999-12-31", designer.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/me/schedule?date=2026-06-15", siteWorker.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Someone_s_overtime_shows_what_made_up_hours_only_to_salary_viewers()
    {
        var (superAdmin, designer, _, _) = await JuneAsync();
        await JoinedInJanuaryAsync(designer);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var url = $"/api/employees/{designer.User.Id}/overtime?month=2026-06";

        var seen = await OkAsync<MyOvertimeResponse>(url, designAdmin);
        seen.ShouldSatisfyAllConditions(
            o => o.OvertimeMinutes.ShouldBe(240),
            o => o.Entries.ShouldBe(2),
            o => o.MadeUpMinutes.ShouldBeNull(),
            o => o.PaidMinutes.ShouldBeNull());
        seen.Days.Select(d => (d.Date, d.DayOff, d.OvertimeMinutes))
            .ShouldBe([(new DateOnly(2026, 6, 20), true, 120), (new DateOnly(2026, 6, 15), false, 120)]);
        seen.Days.SelectMany(d => d.Entries).ShouldAllBe(e => e.MadeUpMinutes == null);

        // June was short, so all of it made up missing hours; salary viewers see that.
        var full = await OkAsync<MyOvertimeResponse>(url, superAdmin);
        full.MadeUpMinutes.ShouldBe(240);
        full.PaidMinutes.ShouldBe(0);
        full.Days.SelectMany(d => d.Entries).Select(e => e.MadeUpMinutes).ShouldBe([120, 120]);

        await (await Client.GetAsync($"/api/employees/{designer.User.Id}/overtime?month=2025-12", designAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync($"/api/employees/{siteWorker.User.Id}/overtime", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(url, designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        (await Client.PostEmptyAsync($"/api/employees/{designer.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<MyOvertimeResponse>(url, designAdmin)).OvertimeMinutes.ShouldBe(240);
    }

    [Fact]
    public async Task Someone_s_hours_are_for_salary_viewers_and_end_with_the_month_they_left()
    {
        var (superAdmin, designer, _, _) = await JuneAsync();
        await JoinedInJanuaryAsync(designer);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var url = $"/api/employees/{designer.User.Id}/hours";

        // Every page, however many months have passed since January 2026.
        var first = await OkAsync<MyHoursResponse>(url, superAdmin);
        var months = new List<MyLedgerMonthResponse>();
        for (var page = 1; page <= (first.TotalMonths + first.PageSize - 1) / first.PageSize; page++)
        {
            months.AddRange((await OkAsync<MyHoursResponse>($"{url}?page={page}", superAdmin)).Months);
        }

        months.Single(m => m.Month == new DateOnly(2026, 6, 1)).ShouldSatisfyAllConditions(
            m => m.ExpectedMinutes.ShouldBe(22 * 420),
            m => m.InsideMinutes.ShouldBe(420),
            m => m.MadeUpMinutes.ShouldBe(240),
            m => m.ShortMinutes.ShouldBe(22 * 420 - 420 - 240));

        // Leaving on Thursday 25 June (15:00 in Beirut): the ledger ends that day, and the history that month.
        (await Client.PostEmptyAsync($"/api/employees/{designer.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await _steps.SetLeftAtAsync(designer.User.Id, "2026-06-25 12:00:00");
        var left = await OkAsync<MyHoursResponse>(url, superAdmin);
        left.TotalMonths.ShouldBe(6);
        left.Months[0].ShouldSatisfyAllConditions(
            m => m.Month.ShouldBe(new DateOnly(2026, 6, 1)),
            m => m.ExpectedMinutes.ShouldBe(19 * 420));

        (await OkAsync<MyHoursResponse>($"/api/employees/{siteWorker.User.Id}/hours", superAdmin)).Applies.ShouldBeFalse();
        await (await Client.GetAsync(url, designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await Client.PutJsonAsync($"/api/users/{designAdmin.User.Id}/permissions",
                new { changes = new[] { new { code = PermissionCodes.SalaryView, action = "grant" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<MyHoursResponse>(url, designAdmin)).TotalMonths.ShouldBe(6);
        await (await Client.GetAsync($"/api/employees/{siteWorker.User.Id}/hours", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    /// <summary>A Design employee with the June work described above, on two new projects.</summary>
    private async Task<(AuthResponse SuperAdmin, AuthResponse Designer, ProjectResponse First, ProjectResponse Second)> JuneAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var first = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var second = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await Work.InsertAsync(designer.User.Id, first.Id, "2026-06-15 06:00:00", "2026-06-15 13:00:00", "Floor plans");
        await Work.InsertAsync(designer.User.Id, second.Id, "2026-06-15 13:00:00", "2026-06-15 15:00:00", "Late render");
        await Work.InsertAsync(designer.User.Id, first.Id, "2026-06-20 06:00:00", "2026-06-20 08:00:00", "Weekend renders");
        return (superAdmin, designer, first, second);
    }

    /// <summary>Moves the joining day to 1 January 2026, so June 2026 is judged in full.</summary>
    private Task JoinedInJanuaryAsync(AuthResponse person)
        => _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");

    private static DateOnly TodayInBeirut() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut));

    /// <summary>Each entry's description and overtime, by description.</summary>
    private static IReadOnlyList<(string Description, int Overtime)> OvertimeOf(IEnumerable<WorkEntryResponse> entries)
        => entries.OrderBy(e => e.Description).Select(e => (e.Description, e.OvertimeMinutes)).ToList();

    private async Task<T> OkAsync<T>(string url, AuthResponse viewer)
    {
        var response = await Client.GetAsync(url, viewer.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }
}
