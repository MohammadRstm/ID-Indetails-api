using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class TimelineEntriesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task The_timeline_lists_a_range_of_days_entries_in_time_order_with_each_person_s_team()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.PutInTeamAsync(anna.User.Id, 2);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        // Beirut is UTC+3 in June: Wednesday 10 June runs from 21:00 UTC on the 9th.
        await Work.InsertAsync(ben.User.Id, project.Id, "2026-06-10 08:00:00", "2026-06-10 10:00:00", "Section drawings");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-06-10 06:00:00", "2026-06-10 09:00:00", "Facade study");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-06-09 20:00:00", "2026-06-09 20:30:00", "Late on the 9th");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-06-11 06:00:00", "2026-06-11 07:00:00", "Next day");

        var response = await Client.GetAsync($"/api/projects/{project.Id}/timeline/entries?from=2026-06-10&to=2026-06-10", admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        var day = await response.ReadAsync<List<TimelineEntryResponse>>();
        day.Select(e => (e.UserId, e.Description, e.Minutes)).ShouldBe([
            (anna.User.Id, "Facade study", 180),
            (ben.User.Id, "Section drawings", 120),
        ]);
        day[0].TeamName.ShouldBe("Design");

        var week = await (await Client.GetAsync($"/api/projects/{project.Id}/timeline/entries?from=2026-06-08&to=2026-06-14", admin.AccessToken))
            .ReadAsync<List<TimelineEntryResponse>>();
        week.Count.ShouldBe(4);

        await (await Client.GetAsync($"/api/projects/{project.Id}/timeline/entries?from=2026-06-10&to=2026-06-09", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync($"/api/projects/{project.Id}/timeline/entries?from=2025-01-01&to=2026-06-10", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync($"/api/projects/{project.Id}/timeline/entries?from=2026-06-10&to=2026-06-10", anna.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{site.Id}/timeline/entries?from=2026-06-10&to=2026-06-10", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }
}
