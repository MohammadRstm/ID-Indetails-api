using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Requirements;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class NotificationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const long ExecutionSiteOperations = 7;

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

        var mine = (await ListAsync(employee.AccessToken)).Items.ShouldHaveSingleItem();
        mine.Kind.ShouldBe(NotificationKinds.RequirementAssigned);
        mine.Actor!.Id.ShouldBe(admin.User.Id);
        mine.Project.ShouldBe(new NotificationProjectResponse(project.Id, project.Title, DomainCodes.Design));
        mine.Payload!.Value.GetProperty("text").GetString().ShouldBe("Issue the stair details");
        mine.ReadAt.ShouldBeNull();
        (await UnreadAsync(employee.AccessToken)).ShouldBe(1);
        (await ListAsync(colleague.AccessToken)).Items.ShouldBeEmpty();

        // Someone else's notification doesn't exist for them.
        await (await Client.PostEmptyAsync($"/api/notifications/{mine.Id}/read", colleague.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await OkAsync<UnreadCountResponse>(Client.PostEmptyAsync($"/api/notifications/{mine.Id}/read", employee.AccessToken)))
            .Unread.ShouldBe(0);
        (await ListAsync(employee.AccessToken, unreadOnly: true)).Items.ShouldBeEmpty();
        (await ListAsync(employee.AccessToken)).Items.ShouldHaveSingleItem().ReadAt.ShouldNotBeNull();

        await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);
        await AssignAsync(project.Id, employee.User.Id, admin.AccessToken);
        (await UnreadAsync(employee.AccessToken)).ShouldBe(2);
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
        var id = (await ListAsync(employee.AccessToken)).Items.ShouldHaveSingleItem().Id;

        var moved = await Client.PutJsonAsync($"/api/employees/{employee.User.Id}/domain",
            new { domainCode = DomainCodes.Execution, teamId = ExecutionSiteOperations }, superAdmin.AccessToken);
        moved.IsSuccessStatusCode.ShouldBeTrue(await moved.Content.ReadAsStringAsync());

        (await ListAsync(employee.AccessToken)).Items.ShouldBeEmpty();
        (await UnreadAsync(employee.AccessToken)).ShouldBe(0);
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

        var all = (await ListAsync(employee.AccessToken)).Items.Select(n => n.Id).ToList();
        all.ShouldBe(all.OrderByDescending(id => id).ToList());
        var second = await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?page=2&pageSize=2", employee.AccessToken));
        second.TotalCount.ShouldBe(3);
        second.Items.ShouldHaveSingleItem().Id.ShouldBe(all[2]);

        await (await Client.GetAsync("/api/notifications?pageSize=0", employee.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
