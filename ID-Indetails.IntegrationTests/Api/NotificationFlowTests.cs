using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Who hears about what: the notifications each change sends.</summary>
public class NotificationFlowTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    private async Task<List<NotificationResponse>> OfKindAsync(string token, string kind)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", token)))
            .Items.Where(n => n.Kind == kind).ToList();

    private async Task<RequirementResponse> AddRequirementAsync(long projectId, string token, params long[] assigneeIds)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/requirements",
            new { text = $"Requirement {Guid.NewGuid():N}"[..20], assigneeIds }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RequirementResponse>();
    }

    [Fact]
    public async Task The_domain_hears_about_new_projects_and_status_changes_while_the_setting_is_on()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        (await OfKindAsync(employee.AccessToken, NotificationKinds.ProjectCreated)).ShouldContain(n => n.Project!.Id == project.Id);
        (await OfKindAsync(admin.AccessToken, NotificationKinds.ProjectCreated)).ShouldNotContain(n => n.Project!.Id == project.Id);
        (await OfKindAsync(siteWorker.AccessToken, NotificationKinds.ProjectCreated)).ShouldNotContain(n => n.Project!.Id == project.Id);
        // Employee broadcasts leave super admins out.
        (await OfKindAsync(superAdmin.AccessToken, NotificationKinds.ProjectCreated)).ShouldNotContain(n => n.Project!.Id == project.Id);

        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", admin.AccessToken));
        var held = (await OfKindAsync(employee.AccessToken, NotificationKinds.ProjectStatusChanged)).ShouldHaveSingleItem();
        held.Payload!.Value.GetProperty("change").GetString().ShouldBe("hold");

        // Execution switches project news off; Design keeps it on for the other tests.
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        (await OfKindAsync(siteWorker.AccessToken, NotificationKinds.ProjectCreated)).ShouldContain(n => n.Project!.Id == site.Id);
        await OkAsync<object>(Client.PutJsonAsync("/api/domain-settings/execution", new { notifyEntry = "all", notifyProject = false }, superAdmin.AccessToken));
        await OkAsync<ProjectResponse>(Client.PostEmptyAsync($"/api/projects/{site.Id}/hold", superAdmin.AccessToken));
        (await OfKindAsync(siteWorker.AccessToken, NotificationKinds.ProjectStatusChanged)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Added_requirements_roll_into_one_notification_and_ticks_reach_assignees_and_managers()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, assignee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        await AddRequirementAsync(project.Id, admin.AccessToken);
        var assigned = await AddRequirementAsync(project.Id, admin.AccessToken, assignee.User.Id);

        var digest = (await OfKindAsync(employee.AccessToken, NotificationKinds.RequirementsAdded)).ShouldHaveSingleItem();
        digest.Count.ShouldBe(2);
        digest.SubjectId.ShouldBe(assigned.Id);
        // The assignee heard about theirs directly, and only about the other one in the digest.
        (await OfKindAsync(assignee.AccessToken, NotificationKinds.RequirementsAdded)).ShouldHaveSingleItem().Count.ShouldBe(1);
        (await OfKindAsync(assignee.AccessToken, NotificationKinds.RequirementAssigned)).ShouldHaveSingleItem();

        await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{assigned.Id}/done", admin.AccessToken));
        (await OfKindAsync(assignee.AccessToken, NotificationKinds.RequirementDone)).ShouldHaveSingleItem().SubjectId.ShouldBe(assigned.Id);
        (await OfKindAsync(superAdmin.AccessToken, NotificationKinds.RequirementDone)).ShouldContain(n => n.SubjectId == assigned.Id);
        (await OfKindAsync(employee.AccessToken, NotificationKinds.RequirementDone)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reaching_90_percent_tells_the_owner_and_whoever_closes_projects_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var only = await AddRequirementAsync(project.Id, admin.AccessToken);

        await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{only.Id}/done", superAdmin.AccessToken));
        await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{only.Id}/undone", superAdmin.AccessToken));
        await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{only.Id}/done", superAdmin.AccessToken));

        // Two crossings, two notifications; the super admin did it, so only the owner hears.
        var nearlyDone = await OfKindAsync(admin.AccessToken, NotificationKinds.ProjectNearlyDone);
        nearlyDone.Count.ShouldBe(2);
        nearlyDone.ShouldAllBe(n => n.Payload!.Value.GetProperty("progressPct").GetInt32() == 100);
        (await OfKindAsync(superAdmin.AccessToken, NotificationKinds.ProjectNearlyDone)).ShouldNotContain(n => n.Project!.Id == project.Id);
    }

    [Fact]
    public async Task Assignees_tick_their_own_requirements_and_managers_hear_about_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, assignee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, other) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var mine = await AddRequirementAsync(project.Id, admin.AccessToken, assignee.User.Id);
        var url = $"/api/projects/{project.Id}/requirements/{mine.Id}";

        await (await Client.PostEmptyAsync($"{url}/done", other.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"{url}/done", assignee.AccessToken))).DoneBy!.Id.ShouldBe(assignee.User.Id);
        (await OfKindAsync(admin.AccessToken, NotificationKinds.RequirementDone)).ShouldHaveSingleItem().Actor!.Id.ShouldBe(assignee.User.Id);
        (await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"{url}/undone", assignee.AccessToken))).IsDone.ShouldBeFalse();

        // What an admin ticked stays ticked for the assignee.
        await OkAsync<RequirementResponse>(Client.PostEmptyAsync($"{url}/done", admin.AccessToken));
        await (await Client.PostEmptyAsync($"{url}/undone", assignee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        // Editing stays with managers.
        await (await Client.PutJsonAsync(url, new { text = "Mine now" }, assignee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task People_hear_about_changes_to_their_own_account_without_amounts()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        // The admin manages Design invitations, and the super admin sent this one.
        (await OfKindAsync(admin.AccessToken, NotificationKinds.InvitationAccepted)).ShouldContain(n => n.SubjectId == employee.User.Id);
        (await OfKindAsync(superAdmin.AccessToken, NotificationKinds.InvitationAccepted)).ShouldContain(n => n.SubjectId == employee.User.Id);

        var pay = await Client.PostJsonAsync($"/api/employees/{employee.User.Id}/pay", new { basis = "monthly", amount = 1500, currency = "usd" }, admin.AccessToken);
        ((int)pay.StatusCode).ShouldBe(201, await pay.Content.ReadAsStringAsync());
        var payChanged = (await OfKindAsync(employee.AccessToken, NotificationKinds.PayChanged)).ShouldHaveSingleItem();
        payChanged.Payload!.Value.TryGetProperty("amount", out _).ShouldBeFalse();

        // Only super admins hold permission:manage.
        await OkAsync<object>(Client.PutJsonAsync($"/api/users/{employee.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ClientView, action = PermissionChangeAction.Grant, reason = "Helps with clients" } } },
            superAdmin.AccessToken));
        var granted = (await OfKindAsync(employee.AccessToken, NotificationKinds.PermissionsChanged)).ShouldHaveSingleItem();
        granted.Payload!.Value.GetProperty("granted")[0].GetString().ShouldBe(PermissionCodes.ClientView);

        await OkAsync<object>(Client.PutJsonAsync($"/api/employees/{employee.User.Id}/domain",
            new { domainCode = DomainCodes.Execution, teamId = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Execution, "Site Operations")).Id }, superAdmin.AccessToken));
        (await OfKindAsync(employee.AccessToken, NotificationKinds.DomainChanged)).ShouldHaveSingleItem()
            .Payload!.Value.GetProperty("to").GetString().ShouldBe(DomainCodes.Execution);
    }

    [Fact]
    public async Task Clients_and_billing_reach_the_people_who_manage_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        (await OfKindAsync(superAdmin.AccessToken, NotificationKinds.ClientAdded)).ShouldContain(n => n.SubjectId == project.Client!.Id);

        var paid = await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 250m, currency = "USD" }, superAdmin.AccessToken);
        ((int)paid.StatusCode).ShouldBe(201, await paid.Content.ReadAsStringAsync());
        var recorded = (await OfKindAsync(admin.AccessToken, NotificationKinds.PaymentRecorded)).ShouldHaveSingleItem();
        recorded.Project!.Id.ShouldBe(project.Id);
        recorded.Payload!.Value.TryGetProperty("amount", out _).ShouldBeFalse();
        (await OfKindAsync(employee.AccessToken, NotificationKinds.PaymentRecorded)).ShouldBeEmpty();
    }
}
