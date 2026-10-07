using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectCostTimelineTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task A_project_s_cost_and_payments_add_up_week_by_week()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        // 1,600 a month is 10 an hour in February 2026 (160 paid hours).
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        await _steps.ExecuteAsync("UPDATE projects SET start_date = '2026-02-09' WHERE id = @id", ("@id", project.Id));

        // Beirut is UTC+2 in February. Tuesday 10 February, 2 hours within hours: 20 plus the paid hour of break.
        // Tuesday 17 February, 1 hour: 10 plus the break, and a 50 expense the next day.
        await Work.InsertAsync(designer.User.Id, project.Id, "2026-02-10 07:00:00", "2026-02-10 09:00:00");
        await Work.InsertAsync(designer.User.Id, project.Id, "2026-02-17 07:00:00", "2026-02-17 08:00:00");
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Prints", amount = 50m, currency = "USD", spentOn = "2026-02-18" }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
            new { amount = 1000m, currency = "USD", method = "bank", receivedOn = "2026-02-11" }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var response = await Client.GetAsync($"/api/projects/{project.Id}/cost-timeline", admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        var timeline = await response.ReadAsync<ProjectCostTimelineResponse>();

        // Weeks start on Monday until the company says otherwise, and run to this week.
        timeline.Currency.ShouldBe("USD");
        timeline.Weeks.Take(2).ShouldBe([
            new CostWeekResponse(new DateOnly(2026, 2, 9), 120, 30m, 30m, 30m, 1000m),
            new CostWeekResponse(new DateOnly(2026, 2, 16), 60, 20m, 70m, 100m, 1000m),
        ]);
        timeline.Weeks[^1].CostToDate.ShouldBe(100m);

        await (await Client.GetAsync($"/api/projects/{project.Id}/cost-timeline", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{project.Id}/cost-timeline", siteAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
