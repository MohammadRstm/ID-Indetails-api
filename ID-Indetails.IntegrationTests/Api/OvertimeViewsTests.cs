using System.Text;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
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
        OvertimeOf(theirs.Items).ShouldBe(new Dictionary<string, int> { ["Floor plans"] = 0, ["Late render"] = 120, ["Weekend renders"] = 120 });

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
        OvertimeOf(project.Recent).ShouldBe(new Dictionary<string, int> { ["Floor plans"] = 0, ["Weekend renders"] = 120 });
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

        // Someone who left keeps their history, and their own sessions end.
        (await Client.PostEmptyAsync($"/api/employees/{designer.User.Id}/deactivate", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<WorkSummaryResponse>($"/api/employees/{designer.User.Id}/work-summary", designAdmin)).TotalOvertimeMinutes.ShouldBe(240);
        await (await Client.GetAsync("/api/me/work-summary", designer.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
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

    private static Dictionary<string, int> OvertimeOf(IEnumerable<WorkEntryResponse> entries)
        => entries.ToDictionary(e => e.Description, e => e.OvertimeMinutes);

    private async Task<T> OkAsync<T>(string url, AuthResponse viewer)
    {
        var response = await Client.GetAsync(url, viewer.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }
}
