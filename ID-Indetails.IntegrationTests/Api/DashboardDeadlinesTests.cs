using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Dashboard;
using ID_Indetails.api.DTO.Response.Requirements;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: the deadlines cover every project of the domain.</summary>
public class DashboardDeadlinesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));

    private async Task<RequirementResponse> AddAsync(long projectId, string token, string text, DateOnly? deadline, params long[] assigneeIds)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/requirements",
            new { text, deadline = deadline?.ToString("yyyy-MM-dd"), assigneeIds }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RequirementResponse>();
    }

    private async Task<UpcomingDeadlinesResponse> DeadlinesAsync(string domain, string token)
    {
        var response = await Client.GetAsync($"/api/dashboard/{domain}/deadlines", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<UpcomingDeadlinesResponse>();
    }

    [Fact]
    public async Task Open_requirements_due_within_two_weeks_and_overdue_ones_are_listed_soonest_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var active = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var held = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await AddAsync(held.Id, admin.AccessToken, "Client sign-off", Today.AddDays(14));
        await AddAsync(active.Id, admin.AccessToken, "Late drawings", Today.AddDays(-3), employee.User.Id);
        await AddAsync(active.Id, admin.AccessToken, "Next month", Today.AddDays(15));
        await AddAsync(active.Id, admin.AccessToken, "No date", null);
        var done = await AddAsync(active.Id, admin.AccessToken, "Already done", Today.AddDays(1));
        (await Client.PostEmptyAsync($"/api/projects/{active.Id}/requirements/{done.Id}/done", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/projects/{held.Id}/hold", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await AddAsync(site.Id, superAdmin.AccessToken, "Site survey", Today.AddDays(1));

        var deadlines = await DeadlinesAsync(DomainCodes.Design, admin.AccessToken);
        deadlines.Total.ShouldBe(2);
        deadlines.Overdue.ShouldBe(1);
        deadlines.Items.Select(i => i.Text).ShouldBe(["Late drawings", "Client sign-off"]);
        var late = deadlines.Items[0];
        late.IsOverdue.ShouldBeTrue();
        late.DaysLeft.ShouldBe(-3);
        late.Project.Id.ShouldBe(active.Id);
        late.Assignees.ShouldHaveSingleItem().Id.ShouldBe(employee.User.Id);
        deadlines.Items[1].DaysLeft.ShouldBe(14);
        deadlines.Items[1].Assignees.ShouldBeEmpty();

        // Only the first 8 are listed; the total counts them all.
        for (var i = 0; i < 7; i++)
        {
            await AddAsync(active.Id, admin.AccessToken, $"Sheet {i}", Today.AddDays(5));
        }

        var many = await DeadlinesAsync(DomainCodes.Design, admin.AccessToken);
        many.Total.ShouldBe(9);
        many.Items.Count.ShouldBe(8);
        many.Items[0].Text.ShouldBe("Late drawings");

        (await DeadlinesAsync(DomainCodes.Execution, superAdmin.AccessToken)).Items.ShouldHaveSingleItem().Text.ShouldBe("Site survey");

        await (await Client.GetAsync("/api/dashboard/design/deadlines", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/dashboard/design/deadlines", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
