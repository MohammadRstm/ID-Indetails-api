using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class WorkEntryReadTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task Admins_list_everyones_entries_and_employees_only_their_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var other = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var start = WorkEntryTestSteps.HoursAgo(8);

        var first = await Work.LogAsync(anna.AccessToken, project.Id, start, 60, "Wireframes for checkout");
        var second = await Work.LogAsync(anna.AccessToken, project.Id, start.AddHours(2), 30, "Review");
        await Work.LogAsync(anna.AccessToken, other.Id, start.AddHours(3), 30, "Other work");
        var bens = await Work.LogAsync(ben.AccessToken, project.Id, start, 45, "Icons");

        var all = await (await Client.GetAsync($"/api/work-entries?projectId={project.Id}", admin.AccessToken)).ReadAsync<PagedResponse<WorkEntryResponse>>();
        all.TotalCount.ShouldBe(3);
        all.Items[0].Id.ShouldBe(second.Id);
        all.Items.ShouldAllBe(e => !e.CanEdit);

        var searched = await (await Client.GetAsync($"/api/work-entries?userId={anna.User.Id}&search=checkout", admin.AccessToken))
            .ReadAsync<PagedResponse<WorkEntryResponse>>();
        searched.Items.Select(e => e.Id).ShouldBe([first.Id]);

        var mine = await (await Client.GetAsync("/api/me/work-entries?pageSize=2", anna.AccessToken)).ReadAsync<PagedResponse<WorkEntryResponse>>();
        mine.TotalCount.ShouldBe(3);
        mine.Items.Count.ShouldBe(2);
        mine.Items.ShouldAllBe(e => e.Person.Id == anna.User.Id && e.CanEdit);

        var detail = await (await Client.GetAsync($"/api/work-entries/{first.Id}", anna.AccessToken)).ReadAsync<WorkEntryDetailResponse>();
        detail.Entry.Id.ShouldBe(first.Id);
        detail.SameDay.Select(e => e.Id).ShouldBe([second.Id]);

        await (await Client.GetAsync($"/api/work-entries/{bens.Id}", anna.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/work-entries", anna.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Admins_comment_the_author_is_told_and_other_domains_see_nothing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var entry = await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(4), 60);

        var response = await Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments", new { body = "  Nice layout  " }, admin.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var comment = await response.ReadAsync<WorkEntryCommentResponse>();
        comment.Body.ShouldBe("Nice layout");
        comment.Author.Role.ShouldBe(RoleCodes.Admin);

        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = 'entry_comment' AND subject_id = @e",
            ("@r", employee.User.Id), ("@e", entry.Id))).ShouldBe(1);

        var read = await (await Client.GetAsync($"/api/work-entries/{entry.Id}/comments", employee.AccessToken))
            .ReadAsync<List<WorkEntryCommentResponse>>();
        read.Single().Id.ShouldBe(comment.Id);
        (await (await Client.GetAsync($"/api/work-entries/{entry.Id}", employee.AccessToken)).ReadAsync<WorkEntryDetailResponse>())
            .Entry.CommentCount.ShouldBe(1);

        await (await Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments", new { body = "Thanks" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments", new { body = "Team", sendToGroup = true }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.GroupChatUnavailable);
        await (await Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments", new { body = " " }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        await (await Client.GetAsync($"/api/work-entries/{entry.Id}", executionAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/work-entries", executionAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments", new { body = "Hi" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
