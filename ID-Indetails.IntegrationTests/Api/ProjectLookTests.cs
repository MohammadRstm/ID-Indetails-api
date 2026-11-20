using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
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
        mine.Code.ShouldBe(project.Code);
        mine.LastActivity!.PersonId.ShouldBe(designer.User.Id);
    }

    [Fact]
    public async Task A_new_project_gets_a_palette_colour_which_an_edit_keeps_unless_it_sends_another()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        ProjectColorRules.Palette.Select(c => c.Hex).ShouldContain(project.Tint.ShouldNotBeNull());

        var kept = await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = project.Title, ownerId = project.Owner.Id }, superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        kept.Tint.ShouldBe(project.Tint);

        var changed = await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = project.Title, ownerId = project.Owner.Id, tint = "#123456" }, superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        changed.Tint.ShouldBe("#123456");

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = project.Title, ownerId = project.Owner.Id, tint = "green" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // a colour given at creation is kept as it is
        var given = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Given colour", billingType = "internal", domainCode = DomainCodes.Design, tint = "#ABCDEF" }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        given.Tint.ShouldBe("#ABCDEF");
    }
}
