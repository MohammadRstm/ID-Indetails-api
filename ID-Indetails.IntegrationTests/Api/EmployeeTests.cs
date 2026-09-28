using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Permissions;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class EmployeeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    // Seeded teams (Constants/TeamSeed.cs).
    private const long DesignEngineering = 1;
    private const long ExecutionSiteOperations = 7;

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<PagedResponse<EmployeeSummaryResponse>> SearchAsync(AuthResponse caller, string query)
        => await OkAsync<PagedResponse<EmployeeSummaryResponse>>(Client.GetAsync($"/api/employees?{query}", caller.AccessToken));

    [Fact]
    public async Task The_list_shows_only_people_of_the_callers_domains()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (designEmail, _, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (siteEmail, _, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        (await SearchAsync(designAdmin, $"search={designEmail}")).TotalCount.ShouldBe(1);
        (await SearchAsync(designAdmin, $"search={siteEmail}")).TotalCount.ShouldBe(0);
        (await SearchAsync(designAdmin, $"role={RoleCodes.SuperAdmin}")).TotalCount.ShouldBe(0);

        (await SearchAsync(superAdmin, $"search={siteEmail}")).Items.ShouldHaveSingleItem().Domain.ShouldBe(DomainCodes.Execution);
        (await SearchAsync(superAdmin, $"role={RoleCodes.SuperAdmin}&pageSize=100")).Items
            .ShouldContain(e => e.Id == superAdmin.User.Id);

        var page = await SearchAsync(superAdmin, "pageSize=2&page=1");
        page.Items.Count.ShouldBe(2);
        page.TotalCount.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task Employees_cannot_list_or_open_other_people()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.GetAsync("/api/employees", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/employees/{superAdmin.User.Id}", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task People_of_another_domain_and_super_admins_read_as_not_found_for_a_domain_admin()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        await (await Client.GetAsync($"/api/employees/{siteWorker.User.Id}", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/employees/{superAdmin.User.Id}", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync($"/api/employees/{siteWorker.User.Id}/deactivate", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Admin_edits_profile_fields_and_team_must_be_in_the_persons_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var url = $"/api/employees/{employee.User.Id}";

        var updated = await OkAsync<EmployeeResponse>(Client.PutJsonAsync(url, new
        {
            firstName = "Maya", lastName = "Saab", phone = "+961 3 000 000", position = "Architect", teamId = DesignEngineering,
        }, designAdmin.AccessToken));
        updated.Position.ShouldBe("Architect");
        updated.Team!.Name.ShouldBe("Engineering");

        await (await Client.PutJsonAsync(url, new { firstName = "Maya", lastName = "Saab", teamId = ExecutionSiteOperations }, designAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.TeamNotInDomain);
        await (await Client.PutJsonAsync(url, new { firstName = "", lastName = "Saab" }, designAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Deactivation_blocks_sign_in_and_reactivation_restores_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (email, password, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var url = $"/api/employees/{employee.User.Id}";

        var deactivated = await OkAsync<EmployeeResponse>(Client.PostEmptyAsync($"{url}/deactivate", designAdmin.AccessToken));
        deactivated.Status.ShouldBe(UserStatus.Deactivated);

        await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await (await Client.GetAsync("/api/auth/me", employee.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @id AND revoked_at IS NULL", ("@id", employee.User.Id))).ShouldBe(0);

        // Hidden from the list unless asked for.
        (await SearchAsync(designAdmin, $"search={email}")).TotalCount.ShouldBe(0);
        (await SearchAsync(designAdmin, $"search={email}&status=deactivated")).TotalCount.ShouldBe(1);

        await (await Client.PostEmptyAsync($"{url}/deactivate", designAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.UserAlreadyDeactivated);

        (await OkAsync<EmployeeResponse>(Client.PostEmptyAsync($"{url}/reactivate", designAdmin.AccessToken)))
            .Status.ShouldBe(UserStatus.Active);
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldSignInAsync();
    }

    [Fact]
    public async Task Nobody_deactivates_themselves_and_admins_cannot_touch_super_admins()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await Client.PostEmptyAsync($"/api/employees/{admin.User.Id}/deactivate", admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);

        // Even an admin who sees both domains needs the super admin invite permission.
        await OkAsync<UserPermissionsResponse>(Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.MaterialCreate, action = "grant" } } }, superAdmin.AccessToken));
        await (await Client.PostEmptyAsync($"/api/employees/{superAdmin.User.Id}/deactivate", admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
    }

    [Fact]
    public async Task A_role_change_clears_personal_permission_changes_and_is_recorded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var id = employee.User.Id;

        var joker = await OkAsync<UserPermissionsResponse>(Client.PutJsonAsync($"/api/users/{id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.SalaryView, action = "grant" } } }, superAdmin.AccessToken));
        joker.IsJoker.ShouldBeTrue();

        var promoted = await OkAsync<EmployeeResponse>(Client.PutJsonAsync($"/api/employees/{id}/role", new { role = RoleCodes.Admin }, designAdmin.AccessToken));
        promoted.Role.ShouldBe(RoleCodes.Admin);
        promoted.Domain.ShouldBe(DomainCodes.Design);
        promoted.IsJoker.ShouldBeFalse();
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM user_permission_overrides WHERE user_id = @id", ("@id", id))).ShouldBe(0);
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = @k AND subject_id = @id", ("@k", ActivityEventKinds.RoleChanged), ("@id", id)))
            .ShouldBe(1);

        // Takes effect on the next request with the old token.
        var me = await OkAsync<CurrentUserResponse>(Client.GetAsync("/api/auth/me", employee.AccessToken));
        me.Role.ShouldBe(RoleCodes.Admin);
        me.Permissions.ShouldNotContain(PermissionCodes.SalaryView);

        await (await Client.PutJsonAsync($"/api/employees/{id}/role", new { role = RoleCodes.Admin }, designAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.RoleUnchanged);
    }

    [Fact]
    public async Task Only_holders_of_the_super_admin_invite_permission_make_or_unmake_super_admins()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var url = $"/api/employees/{employee.User.Id}/role";

        await (await Client.PutJsonAsync(url, new { role = RoleCodes.SuperAdmin }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);

        var made = await OkAsync<EmployeeResponse>(Client.PutJsonAsync(url, new { role = RoleCodes.SuperAdmin }, superAdmin.AccessToken));
        made.Role.ShouldBe(RoleCodes.SuperAdmin);
        made.Domain.ShouldBeNull();

        await (await Client.PutJsonAsync(url, new { role = RoleCodes.Admin }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var unmade = await OkAsync<EmployeeResponse>(Client.PutJsonAsync(url, new { role = RoleCodes.Admin, domainCode = DomainCodes.Execution }, superAdmin.AccessToken));
        unmade.Domain.ShouldBe(DomainCodes.Execution);
    }

    [Fact]
    public async Task Moving_someone_to_the_other_domain_needs_both_domains_and_resets_their_team()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var url = $"/api/employees/{employee.User.Id}/domain";

        await (await Client.PutJsonAsync(url, new { domainCode = DomainCodes.Execution }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        await (await Client.PutJsonAsync(url, new { domainCode = DomainCodes.Execution, teamId = DesignEngineering }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.TeamNotInDomain);

        var moved = await OkAsync<EmployeeResponse>(Client.PutJsonAsync(url,
            new { domainCode = DomainCodes.Execution, teamId = ExecutionSiteOperations }, superAdmin.AccessToken));
        moved.Domain.ShouldBe(DomainCodes.Execution);
        moved.Team!.Name.ShouldBe("Site Operations");

        // Now out of the design admin's reach.
        await (await Client.GetAsync($"/api/employees/{employee.User.Id}", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Pay_changes_only_add_to_the_end_of_the_history()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var url = $"/api/employees/{employee.User.Id}/pay";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await Client.PostJsonAsync(url,
            new { basis = "monthly", amount = 1000, currency = "usd", effectiveFrom = today.AddMonths(-3) }, designAdmin.AccessToken);
        ((int)first.StatusCode).ShouldBe(201, await first.Content.ReadAsStringAsync());
        (await first.ReadAsync<PayResponse>()).HourlyCostRate.ShouldBe(5.7693m);

        await (await Client.PostJsonAsync(url, new { basis = "monthly", amount = 1200, effectiveFrom = today.AddMonths(-4) }, designAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.PayChangeNotAfterLatest);
        await (await Client.PostJsonAsync(url, new { basis = "monthly", amount = 1200, effectiveFrom = today.AddDays(2) }, designAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PayDateInFuture);
        await (await Client.PostJsonAsync($"/api/employees/{designAdmin.User.Id}/pay", new { basis = "hourly", amount = 50 }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);

        ((int)(await Client.PostJsonAsync(url, new { basis = "hourly", amount = 12.5, note = "Switched to hourly" }, designAdmin.AccessToken)).StatusCode)
            .ShouldBe(201);

        var history = await OkAsync<List<PayResponse>>(Client.GetAsync(url, designAdmin.AccessToken));
        history.Select(p => p.Amount).ShouldBe([12.5m, 1000m]);

        var profile = await OkAsync<EmployeeResponse>(Client.GetAsync($"/api/employees/{employee.User.Id}", designAdmin.AccessToken));
        profile.CurrentPay!.Basis.ShouldBe(PayBasis.Hourly);

        await (await Client.GetAsync(url, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task People_edit_their_own_name_and_phone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var me = await OkAsync<CurrentUserResponse>(Client.PutJsonAsync("/api/me/profile",
            new { firstName = " Omar ", lastName = "Nassar", phone = "+961 71 111 111" }, employee.AccessToken));
        me.FirstName.ShouldBe("Omar");
        me.LastName.ShouldBe("Nassar");

        await (await Client.PutJsonAsync("/api/me/profile", new { firstName = "", lastName = "Nassar" }, employee.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
