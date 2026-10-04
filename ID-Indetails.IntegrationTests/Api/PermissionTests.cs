using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Permissions;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class PermissionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static object Changes(PermissionChangeAction action, params string[] codes)
        => new { changes = codes.Select(code => new { code, action, reason = "Integration test" }) };

    private Task<HttpResponseMessage> ChangeAsync(AuthResponse actor, long userId, PermissionChangeAction action, params string[] codes)
        => Client.PutJsonAsync($"/api/users/{userId}/permissions", Changes(action, codes), actor.AccessToken);

    private async Task<UserPermissionsResponse> ShouldChangeAsync(
        AuthResponse actor, long userId, PermissionChangeAction action, params string[] codes)
    {
        var response = await ChangeAsync(actor, userId, action, codes);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<UserPermissionsResponse>();
    }

    private async Task<UserPermissionsResponse> PermissionsOfAsync(AuthResponse actor, long userId)
    {
        var response = await Client.GetAsync($"/api/users/{userId}/permissions", actor.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<UserPermissionsResponse>();
    }

    [Fact]
    public async Task A_grant_and_a_revoke_take_effect_on_the_very_next_request()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.GetAsync("/api/permissions", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var granted = await ShouldChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Grant, PermissionCodes.PermissionManage);
        granted.IsJoker.ShouldBeTrue();
        granted.Role.ShouldBe(RoleCodes.Employee);
        granted.Overrides.ShouldHaveSingleItem().Effect.ShouldBe(PermissionEffect.Grant);

        // Same access token: permissions are read from the database, not the token.
        (await Client.GetAsync("/api/permissions", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var me = await (await Client.GetAsync("/api/auth/me", employee.AccessToken)).ReadAsync<CurrentUserResponse>();
        me.IsJoker.ShouldBeTrue();
        me.Permissions.ShouldContain(PermissionCodes.PermissionManage);

        var reset = await ShouldChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Reset, PermissionCodes.PermissionManage);
        reset.IsJoker.ShouldBeFalse();
        reset.Overrides.ShouldBeEmpty();

        await (await Client.GetAsync("/api/permissions", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Everyone_reads_their_own_permissions_with_what_each_allows_and_where_it_comes_from()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var taken = employee.User.Permissions.First();
        await ShouldChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Grant, PermissionCodes.ClientCreate);
        await ShouldChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Revoke, taken);

        var mine = await (await Client.GetAsync("/api/me/permissions", employee.AccessToken)).ReadAsync<List<MyPermissionResponse>>();
        var me = await (await Client.GetAsync("/api/auth/me", employee.AccessToken)).ReadAsync<CurrentUserResponse>();
        mine.Where(p => p.Source != PermissionSource.Removed).Select(p => p.Code).ShouldBe(me.Permissions, ignoreOrder: true);
        mine.Single(p => p.Code == taken).Source.ShouldBe(PermissionSource.Removed);
        mine.ShouldAllBe(p => p.Description.Length > 0 && p.Explanation.Length > 0);
        mine.Single(p => p.Code == PermissionCodes.ClientCreate).Source.ShouldBe(PermissionSource.Granted);
        mine.Where(p => p.Code != PermissionCodes.ClientCreate && p.Code != taken).ShouldAllBe(p => p.Source == PermissionSource.Role);

        // a super admin holds every one, all from the role
        var catalog = await (await Client.GetAsync("/api/permissions", superAdmin.AccessToken)).ReadAsync<List<PermissionDefinitionResponse>>();
        var all = await (await Client.GetAsync("/api/me/permissions", superAdmin.AccessToken)).ReadAsync<List<MyPermissionResponse>>();
        all.Count.ShouldBe(catalog.Count);
        all.ShouldAllBe(p => p.Source == PermissionSource.Role);
        catalog.ShouldAllBe(p => p.Explanation.Length > 0);
    }

    [Fact]
    public async Task A_revoked_default_is_refused_immediately()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var invite = new { email = AuthTestSteps.NewEmail(), role = RoleCodes.Employee };

        await ShouldChangeAsync(superAdmin, admin.User.Id, PermissionChangeAction.Revoke, PermissionCodes.EmployeeInvite);

        await (await Client.PostJsonAsync("/api/invitations", invite, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Callers_without_permission_manage_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.GetAsync($"/api/users/{employee.User.Id}/permissions", admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await ChangeAsync(admin, employee.User.Id, PermissionChangeAction.Grant, PermissionCodes.ClientView))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_super_admin_can_never_be_changed_and_nobody_changes_themselves()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, otherSuperAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.SuperAdmin, null);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await ChangeAsync(superAdmin, otherSuperAdmin.User.Id, PermissionChangeAction.Revoke, PermissionCodes.ClientView))
            .ShouldFailWithAsync(403, ErrorCodes.PermissionsLocked);

        var view = await PermissionsOfAsync(superAdmin, otherSuperAdmin.User.Id);
        view.IsLocked.ShouldBeTrue();
        view.Permissions.Count.ShouldBe(PermissionCatalog.Definitions.Count);

        await ShouldChangeAsync(superAdmin, admin.User.Id, PermissionChangeAction.Grant, PermissionCodes.PermissionManage);
        await (await ChangeAsync(admin, admin.User.Id, PermissionChangeAction.Grant, PermissionCodes.ClientView))
            .ShouldFailWithAsync(403, ErrorCodes.PermissionChangeNotAllowed);
    }

    [Fact]
    public async Task A_delegate_can_only_hand_out_permissions_they_hold()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ShouldChangeAsync(superAdmin, admin.User.Id, PermissionChangeAction.Grant, PermissionCodes.PermissionManage);

        await (await ChangeAsync(admin, employee.User.Id, PermissionChangeAction.Grant, PermissionCodes.SalaryView))
            .ShouldFailWithAsync(403, ErrorCodes.PermissionChangeNotAllowed);

        var granted = await ShouldChangeAsync(admin, employee.User.Id, PermissionChangeAction.Grant, PermissionCodes.ClientView);
        granted.Permissions.ShouldContain(PermissionCodes.ClientView);
        granted.Overrides.ShouldHaveSingleItem().GrantedById.ShouldBe(admin.User.Id);
    }

    [Fact]
    public async Task Other_domain_users_are_hidden_until_the_caller_holds_a_permission_of_that_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        await ShouldChangeAsync(superAdmin, designAdmin.User.Id, PermissionChangeAction.Grant, PermissionCodes.PermissionManage);

        await (await Client.GetAsync($"/api/users/{siteWorker.User.Id}/permissions", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var crossDomain = await ShouldChangeAsync(superAdmin, designAdmin.User.Id, PermissionChangeAction.Grant, PermissionCodes.MaterialCreate);
        crossDomain.VisibleDomains.ShouldBe([DomainCodes.Design, DomainCodes.Execution]);

        (await PermissionsOfAsync(designAdmin, siteWorker.User.Id)).Domain.ShouldBe(DomainCodes.Execution);
    }

    [Fact]
    public async Task Exactly_matching_another_role_changes_the_role_and_is_recorded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var adminDefaults = (await PermissionsOfAsync(superAdmin, admin.User.Id)).RoleDefaults;
        var employeeDefaults = (await PermissionsOfAsync(superAdmin, employee.User.Id)).RoleDefaults;
        var missing = adminDefaults.Except(employeeDefaults).ToArray();

        var promoted = await ShouldChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Grant, missing);
        promoted.Role.ShouldBe(RoleCodes.Admin);
        promoted.PreviousRole.ShouldBe(RoleCodes.Employee);
        promoted.Overrides.ShouldBeEmpty();
        promoted.IsJoker.ShouldBeFalse();

        (await _steps.ScalarAsync<string>("SELECT role_code FROM users WHERE id = @id", ("@id", employee.User.Id)))
            .ShouldBe(RoleCodes.Admin);
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = @k AND subject_type = 'user' AND subject_id = @id",
            ("@k", ActivityEventKinds.RoleAutoPromoted), ("@id", employee.User.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = @k AND subject_id = @id",
            ("@k", ActivityEventKinds.PermissionGranted), ("@id", employee.User.Id))).ShouldBe(missing.Length);

        var me = await (await Client.GetAsync("/api/auth/me", employee.AccessToken)).ReadAsync<CurrentUserResponse>();
        me.Role.ShouldBe(RoleCodes.Admin);

        var demoted = await ShouldChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Revoke, missing);
        demoted.Role.ShouldBe(RoleCodes.Employee);
        demoted.PreviousRole.ShouldBe(RoleCodes.Admin);
        demoted.Overrides.ShouldBeEmpty();
    }

    [Fact]
    public async Task Inviting_a_super_admin_needs_its_own_permission_and_every_permission_a_super_admin_holds()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        object Invite() => new { email = AuthTestSteps.NewEmail("super"), role = RoleCodes.SuperAdmin };

        await (await Client.PostJsonAsync("/api/invitations", Invite(), admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);

        await ShouldChangeAsync(superAdmin, admin.User.Id, PermissionChangeAction.Grant, PermissionCodes.EmployeeInviteSuperAdmin);

        await (await Client.PostJsonAsync("/api/invitations", Invite(), admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);
        ((int)(await Client.PostJsonAsync("/api/invitations", Invite(), superAdmin.AccessToken)).StatusCode).ShouldBe(201);
    }

    [Fact]
    public async Task Unknown_permissions_and_malformed_requests_are_rejected()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await ChangeAsync(superAdmin, employee.User.Id, PermissionChangeAction.Grant, "nuke:everything"))
            .ShouldFailWithAsync(400, ErrorCodes.UnknownPermission);
        await (await Client.PutJsonAsync($"/api/users/{employee.User.Id}/permissions", new { changes = Array.Empty<object>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await ChangeAsync(superAdmin, 999_999_999, PermissionChangeAction.Grant, PermissionCodes.ClientView))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Catalogue_lists_every_permission_with_its_role_defaults()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        var catalog = await (await Client.GetAsync("/api/permissions", superAdmin.AccessToken))
            .ReadAsync<List<PermissionDefinitionResponse>>();

        catalog.Count.ShouldBe(PermissionCatalog.Definitions.Count);
        catalog.Single(p => p.Code == PermissionCodes.PermissionManage).DefaultFor.ShouldBe([RoleCodes.SuperAdmin]);
        catalog.Single(p => p.Code == PermissionCodes.ProjectView).DefaultFor
            .ShouldBe([RoleCodes.SuperAdmin, RoleCodes.Admin, RoleCodes.Employee]);
    }
}
