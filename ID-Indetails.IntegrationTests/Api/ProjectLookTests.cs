using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectLookTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Entries_and_my_projects_carry_the_project_s_icon_and_tint_and_who_was_last_on_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var work = new WorkEntryTestSteps(_steps);
        await work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await _steps.ExecuteAsync("UPDATE projects SET icon = 'compass', tint = '#3366ff' WHERE id = @id", ("@id", project.Id));

        var entry = await work.LogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60);
        entry.Project.ShouldBe(new WorkEntryProjectResponse(project.Id, project.Title, "compass", "#3366ff"));
        await work.InsertAsync(colleague.User.Id, project.Id, "2026-06-10 06:00:00", "2026-06-10 07:00:00");

        var response = await Client.GetAsync("/api/me/projects", designer.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        var mine = (await response.ReadAsync<List<MyProjectResponse>>()).Single(p => p.Id == project.Id);
        mine.Icon.ShouldBe("compass");
        mine.Tint.ShouldBe("#3366ff");
        mine.LastActivity!.PersonId.ShouldBe(designer.User.Id);
    }
}
