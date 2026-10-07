using System.Text;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class WorkEntryExportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private static async Task<string[]> LinesAsync(HttpResponseMessage response)
    {
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Admins_export_timesheets_and_only_money_viewers_get_the_cost_columns()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, lead) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "hourly", amount = 20m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PutJsonAsync($"/api/users/{lead.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WorkEntryViewAll, action = "grant", reason = "Team lead" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var entry = await Work.LogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(5), 90, "Logo, second round");

        var lines = await LinesAsync(await Client.GetAsync($"/api/work-entries/export?projectId={project.Id}", admin.AccessToken));
        lines[0].ShouldBe("Date,Weekday,Start,End,Hours,Overtime hours,Employee,Email,Team,Project,Client,Task,Hourly rate,Currency,Labour cost,Entry id");
        lines.Length.ShouldBe(2);
        lines[1].ShouldContain(",1.50,");
        lines[1].ShouldContain("\"Logo, second round\",20.00,USD,30.00," + entry.Id);

        var withoutMoney = await LinesAsync(await Client.GetAsync($"/api/work-entries/export?projectId={project.Id}", lead.AccessToken));
        withoutMoney[0].ShouldBe("Date,Weekday,Start,End,Hours,Overtime hours,Employee,Email,Team,Project,Client,Task,Entry id");
        withoutMoney[1].ShouldEndWith("\"Logo, second round\"," + entry.Id);

        await (await Client.GetAsync("/api/work-entries/export", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        await (await Client.GetAsync("/api/work-entries/export", executionAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Exports_are_rate_limited_per_user()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
        {
            last = await Client.GetAsync("/api/work-entries/export?from=2020-01-01&to=2020-01-01", admin.AccessToken);
        }

        await last!.ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        (await Client.GetAsync("/api/work-entries/export?from=2020-01-01&to=2020-01-01", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }
}
