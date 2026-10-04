using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Nobody hands out more than they hold, and hours, pay and super admins stay with those allowed to see them.</summary>
public class PrivilegeAndPrivacyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private async Task ChangeAsync(AuthResponse superAdmin, long userId, string action, params string[] codes)
    {
        var changed = await Client.PutJsonAsync($"/api/users/{userId}/permissions",
            new { changes = codes.Select(code => new { code, action, reason = "Test" }).ToArray() }, superAdmin.AccessToken);
        changed.IsSuccessStatusCode.ShouldBeTrue(await changed.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> InviteAsync(AuthResponse inviter, string role, object? pay = null)
        => Client.PostJsonAsync("/api/invitations", new { email = AuthTestSteps.NewEmail(role), role, domainCode = DomainCodes.Design, pay }, inviter.AccessToken);

    [Fact]
    public async Task Inviting_or_promoting_someone_needs_the_permissions_of_their_new_role()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, recruiter) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ChangeAsync(superAdmin, recruiter.User.Id, "grant", PermissionCodes.EmployeeInvite, PermissionCodes.EmployeeView, PermissionCodes.EmployeeManage);

        // an employee may bring in employees, never an admin (their own second mailbox, say)
        ((int)(await InviteAsync(recruiter, RoleCodes.Employee)).StatusCode).ShouldBe(201);
        await (await InviteAsync(recruiter, RoleCodes.Admin)).ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);
        await (await Client.PutJsonAsync($"/api/employees/{colleague.User.Id}/role", new { role = RoleCodes.Admin }, recruiter.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.PermissionChangeNotAllowed);
    }

    [Fact]
    public async Task A_role_change_gives_back_permissions_taken_away_by_hand_only_for_someone_who_manages_permissions()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, limited) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, demoted) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        // meeting:create comes with the employee role too
        await ChangeAsync(superAdmin, limited.User.Id, "revoke", PermissionCodes.MeetingCreate);
        await ChangeAsync(superAdmin, demoted.User.Id, "revoke", PermissionCodes.FinancialView);

        await (await Client.PutJsonAsync($"/api/employees/{limited.User.Id}/role", new { role = RoleCodes.Employee }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.PermissionChangeNotAllowed);
        (await Client.PutJsonAsync($"/api/employees/{limited.User.Id}/role", new { role = RoleCodes.Employee }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        // a role that doesn't give the permission back is anyone's to hand out
        (await Client.PutJsonAsync($"/api/employees/{demoted.User.Id}/role", new { role = RoleCodes.Employee }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Setting_pay_on_an_invitation_needs_the_permission_to_change_pay()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        await ChangeAsync(superAdmin, admin.User.Id, "revoke", PermissionCodes.EmployeePayManage);

        await (await InviteAsync(admin, RoleCodes.Employee, new { basis = "hourly", amount = 10m, currency = "USD" }))
            .ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);
        ((int)(await InviteAsync(admin, RoleCodes.Employee)).StatusCode).ShouldBe(201);
    }

    [Fact]
    public async Task Nobody_excuses_their_own_hours_or_sets_their_own_schedule()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var self = superAdmin.User.Id;

        await (await Client.PostJsonAsync($"/api/employees/{self}/hour-excuses",
            new { month = "2026-06", kind = "days", days = new[] { "2026-06-02" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        await (await Client.PutJsonAsync($"/api/employees/{self}/schedule",
            new { days = new[] { new { weekday = 1, startMinute = 480, endMinute = 600 } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
    }

    [Fact]
    public async Task Super_admins_their_hours_and_their_pay_stay_with_people_who_see_both_domains_and_salaries()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await Client.GetAsync($"/api/employees/{superAdmin.User.Id}/work-summary", admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // seeing both domains shows super admins, but their pay is a company matter
        await ChangeAsync(superAdmin, admin.User.Id, "grant", PermissionCodes.MaterialCreate);
        await (await Client.GetAsync($"/api/employees/{superAdmin.User.Id}/pay", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await ChangeAsync(superAdmin, admin.User.Id, "grant", PermissionCodes.SalaryView);
        (await Client.GetAsync($"/api/employees/{superAdmin.User.Id}/pay", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Project_pay_leaves_out_people_the_reader_cant_see()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ChangeAsync(superAdmin, admin.User.Id, "grant", PermissionCodes.SalaryView);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Work.LogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60, "Plans");
        await Work.LogAsync(superAdmin.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(6), 60, "Review");

        var pay = await Client.GetAsync("/api/company/project-pay", admin.AccessToken);
        ((int)pay.StatusCode).ShouldBe(200, await pay.Content.ReadAsStringAsync());
        var text = await pay.Content.ReadAsStringAsync();
        text.ShouldContain($"\"userId\":{designer.User.Id},");
        text.ShouldNotContain($"\"userId\":{superAdmin.User.Id},");
    }

    [Fact]
    public async Task Moving_someone_to_the_other_domain_needs_its_permissions()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        // one Execution permission opens Execution to them, not its admin defaults
        await ChangeAsync(superAdmin, admin.User.Id, "grant", PermissionCodes.MaterialCreate);

        await (await Client.PutJsonAsync($"/api/employees/{colleague.User.Id}/domain", new { domainCode = DomainCodes.Execution }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.PermissionChangeNotAllowed);
        (await Client.PutJsonAsync($"/api/employees/{colleague.User.Id}/domain", new { domainCode = DomainCodes.Execution }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Ranking_by_hours_and_pricing_a_new_site_need_their_permissions()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ChangeAsync(superAdmin, employee.User.Id, "grant", PermissionCodes.ClientView);
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        await ChangeAsync(superAdmin, siteAdmin.User.Id, "revoke", PermissionCodes.ProjectPricingManage);

        await (await Client.GetAsync("/api/projects?sort=hours", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/clients?sort=hours", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Lobby", domainCode = DomainCodes.Execution, clientId = client.Id, projectFee = 1000m, materialMarkup = 0.10m }, siteAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Commenting_on_work_needs_reading_it_and_old_notifications_follow_the_permission_they_were_sent_under()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, notifyEntry: "all");
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, author) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, commenter) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ChangeAsync(superAdmin, commenter.User.Id, "grant", PermissionCodes.WorkEntryComment);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var entry = await Work.LogAsync(author.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60, "Facade study");

        await (await Client.PostJsonAsync($"/api/work-entries/{entry.Id}/comments", new { body = "Nice" }, commenter.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        async Task<int> WorkLoggedAsync()
            => (await (await Client.GetAsync("/api/notifications?pageSize=100", admin.AccessToken)).ReadAsync<PagedResponse<NotificationResponse>>())
                .Items.Count(n => n.Kind == NotificationKinds.WorkLogged);
        (await WorkLoggedAsync()).ShouldBe(1);
        await ChangeAsync(superAdmin, admin.User.Id, "revoke", PermissionCodes.WorkEntryViewAll);
        (await WorkLoggedAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Hours_excused_show_in_the_feed_only_to_people_who_see_salaries()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/hour-excuses",
            new { month = "2026-06", kind = "days", days = new[] { "2026-06-02" } }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        async Task<bool> SeesAsync(AuthResponse reader)
            => (await (await Client.GetAsync("/api/activity?pageSize=100", reader.AccessToken)).ReadAsync<PagedResponse<ActivityItemResponse>>())
                .Items.Any(e => e.Kind == ActivityEventKinds.HoursExcused && e.SubjectId == designer.User.Id);
        (await SeesAsync(admin)).ShouldBeFalse();
        await ChangeAsync(superAdmin, admin.User.Id, "grant", PermissionCodes.SalaryView);
        (await SeesAsync(admin)).ShouldBeTrue();
    }
}
