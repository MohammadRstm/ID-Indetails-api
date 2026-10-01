using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Requirements;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class NotificationTests(ApiFactory api) : IClassFixture<ApiFactory>
{

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<PagedResponse<NotificationResponse>> ListAsync(string token, bool unreadOnly = false)
        => OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync($"/api/notifications?unreadOnly={unreadOnly}", token));

    private async Task<List<NotificationResponse>> AssignedAsync(string token)
        => (await ListAsync(token)).Items.Where(n => n.Kind == NotificationKinds.RequirementAssigned).ToList();

    private async Task<int> UnreadAsync(string token)
        => (await OkAsync<UnreadCountResponse>(Client.GetAsync("/api/notifications/unread-count", token))).Unread;

    private async Task AssignAsync(long projectId, long userId, string token)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/requirements",
            new { text = "Issue the stair details", assigneeIds = new[] { userId } }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        await response.ReadAsync<RequirementResponse>();
    }

    [Fact]
    public async Task People_read_and_clear_only_their_own_notifications()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);

        var mine = (await AssignedAsync(employee.AccessToken)).ShouldHaveSingleItem();
        mine.Actor!.Id.ShouldBe(admin.User.Id);
        mine.Project.ShouldBe(new NotificationProjectResponse(project.Id, project.Title, DomainCodes.Design, project.Icon, project.Tint));
        mine.Payload!.Value.GetProperty("text").GetString().ShouldBe("Issue the stair details");
        mine.ReadAt.ShouldBeNull();
        // The new project, and the assignment.
        (await UnreadAsync(employee.AccessToken)).ShouldBe(2);
        (await AssignedAsync(colleague.AccessToken)).ShouldBeEmpty();

        // Someone else's notification doesn't exist for them.
        await (await Client.PostEmptyAsync($"/api/notifications/{mine.Id}/read", colleague.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await OkAsync<UnreadCountResponse>(Client.PostEmptyAsync($"/api/notifications/{mine.Id}/read", employee.AccessToken)))
            .Unread.ShouldBe(1);
        (await ListAsync(employee.AccessToken, unreadOnly: true)).Items.ShouldHaveSingleItem().Kind.ShouldBe(NotificationKinds.ProjectCreated);
        (await AssignedAsync(employee.AccessToken)).ShouldHaveSingleItem().ReadAt.ShouldNotBeNull();

        await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);
        await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);
        (await UnreadAsync(employee.AccessToken)).ShouldBe(3);
        (await OkAsync<UnreadCountResponse>(Client.PostEmptyAsync("/api/notifications/read-all", employee.AccessToken)))
            .Unread.ShouldBe(0);
        (await ListAsync(employee.AccessToken, unreadOnly: true)).Items.ShouldBeEmpty();

        (await Client.GetAsync("/api/notifications", "not-a-token")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Notifications_about_a_domain_you_left_are_hidden()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);
        var id = (await AssignedAsync(employee.AccessToken)).ShouldHaveSingleItem().Id;

        var moved = await Client.PutJsonAsync($"/api/employees/{employee.User.Id}/domain",
            new { domainCode = DomainCodes.Execution, teamId = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Execution, "Site Operations")).Id }, superAdmin.AccessToken);
        moved.IsSuccessStatusCode.ShouldBeTrue(await moved.Content.ReadAsStringAsync());

        // What's left is the move itself, which belongs to no project.
        (await ListAsync(employee.AccessToken)).Items.ShouldHaveSingleItem().Kind.ShouldBe(NotificationKinds.DomainChanged);
        (await UnreadAsync(employee.AccessToken)).ShouldBe(1);
        await (await Client.PostEmptyAsync($"/api/notifications/{id}/read", employee.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Pages_are_newest_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        for (var i = 0; i < 3; i++)
        {
            await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);
        }

        // The new project, and three assignments.
        var all = (await ListAsync(employee.AccessToken)).Items.Select(n => n.Id).ToList();
        all.Count.ShouldBe(4);
        all.ShouldBe(all.OrderByDescending(id => id).ToList());
        var second = await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?page=2&pageSize=2", employee.AccessToken));
        second.TotalCount.ShouldBe(4);
        second.Items.Select(n => n.Id).ShouldBe(all.Skip(2));

        await (await Client.GetAsync("/api/notifications?pageSize=0", employee.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
