using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.DTO.Response.Labour;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SiteTeamTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<ProjectResponse> NewSiteAsync(string token, params long[] staffIds)
    {
        var client = await Projects.CreateClientAsync(token, DomainCodes.Execution);
        var created = await Client.PostJsonAsync("/api/projects",
            new { title = "Site " + Guid.NewGuid().ToString("N")[..8], domainCode = DomainCodes.Execution, clientId = client.Id, staffIds }, token);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    [Fact]
    public async Task A_site_is_created_with_the_employees_who_oversee_it_and_that_is_its_team()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var site = await NewSiteAsync(siteAdmin.AccessToken, rami.User.Id, layla.User.Id);
        site.Staff!.Select(s => s.Id).ShouldBe([rami.User.Id, layla.User.Id]);
        site.Team!.Count.ShouldBe(2);
        site.Team.People.Select(p => p.Id).ShouldBe([rami.User.Id, layla.User.Id]);

        var row = (await (await Client.GetAsync("/api/projects?domain=execution", siteAdmin.AccessToken))
            .ReadAsync<PagedResponse<ProjectSummaryResponse>>()).Items.Single(p => p.Id == site.Id);
        row.Team!.Count.ShouldBe(2);

        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var designDetail = await (await Client.GetAsync($"/api/projects/{design.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        designDetail.Team.ShouldBeNull();
        designDetail.Staff.ShouldBeNull();
    }

    [Fact]
    public async Task A_site_needs_at_least_one_active_execution_employee_to_oversee_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);

        async Task RefusedAsync(long[] staffIds)
        {
            var response = await Client.PostJsonAsync("/api/projects",
                new { title = "Site " + Guid.NewGuid().ToString("N")[..8], domainCode = DomainCodes.Execution, clientId = client.Id, staffIds }, superAdmin.AccessToken);
            (await response.ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("staffIds");
        }

        await RefusedAsync([]);
        await RefusedAsync([designer.User.Id]);
        await RefusedAsync([siteAdmin.User.Id]);
        await RefusedAsync([rami.User.Id, rami.User.Id]);
        await RefusedAsync([999_999_999L]);

        // no staff on a Design project
        var designClient = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await (await Client.PostJsonAsync("/api/projects",
                new { title = "Villa", domainCode = DomainCodes.Design, clientId = designClient.Id, staffIds = new[] { rami.User.Id } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("staffIds");

        // someone who left can't be put on a site
        await _steps.ExecuteAsync("UPDATE users SET status = 'deactivated', deactivated_at = UTC_TIMESTAMP(3) WHERE id = @id", ("@id", rami.User.Id));
        await RefusedAsync([rami.User.Id]);
    }

    [Fact]
    public async Task Admins_change_who_oversees_a_site_and_someone_taken_off_and_put_back_keeps_one_row()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await NewSiteAsync(superAdmin.AccessToken, rami.User.Id);

        async Task<ProjectResponse> SetAsync(string token, params long[] staffIds)
        {
            var response = await Client.PutJsonAsync($"/api/projects/{site.Id}/staff", new { staffIds }, token);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<ProjectResponse>();
        }

        (await SetAsync(siteAdmin.AccessToken, layla.User.Id)).Staff!.Select(s => s.Id).ShouldBe([layla.User.Id]);
        (await SetAsync(superAdmin.AccessToken, layla.User.Id, rami.User.Id)).Staff!.Select(s => s.Id).ShouldBe([layla.User.Id, rami.User.Id]);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM project_staff WHERE project_id = @p AND user_id = @u", ("@p", site.Id), ("@u", rami.User.Id)))
            .ShouldBe(1);

        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/staff", new { staffIds = Array.Empty<long>() }, siteAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/staff", new { staffIds = new[] { rami.User.Id } }, rami.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await (await Client.PutJsonAsync($"/api/projects/{design.Id}/staff", new { staffIds = new[] { rami.User.Id } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task Only_a_sites_staff_report_its_labour_and_the_sites_they_oversee_are_listed_for_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await NewSiteAsync(superAdmin.AccessToken, rami.User.Id);
        var other = await NewSiteAsync(superAdmin.AccessToken, layla.User.Id);

        var weeks = await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks", layla.AccessToken)).ReadAsync<LabourWeekListResponse>();
        weeks.CanReport.ShouldBeFalse();
        var draft = await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks/{weeks.Weeks[0].WeekStart:yyyy-MM-dd}", superAdmin.AccessToken))
            .ReadAsync<LabourWeekResponse>();
        var body = new { days = new[] { new { date = draft.Days[^1].Date, workers = 4, dailyPay = 40m } } };
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{draft.WeekStart:yyyy-MM-dd}", body, layla.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.NotOnSiteTeam);

        (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks", rami.AccessToken)).ReadAsync<LabourWeekListResponse>()).CanReport.ShouldBeTrue();
        var reported = await Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{draft.WeekStart:yyyy-MM-dd}", body, rami.AccessToken);
        ((int)reported.StatusCode).ShouldBe(200, await reported.Content.ReadAsStringAsync());

        (await (await Client.GetAsync("/api/me/sites", rami.AccessToken)).ReadAsync<List<MySiteResponse>>()).Select(s => s.Id).ShouldBe([site.Id]);
        (await (await Client.GetAsync("/api/me/sites", layla.AccessToken)).ReadAsync<List<MySiteResponse>>()).Select(s => s.Id).ShouldBe([other.Id]);
    }

    [Fact]
    public async Task Execution_employees_record_no_materials_unless_given_it_and_are_paid_monthly()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await NewSiteAsync(superAdmin.AccessToken, rami.User.Id);

        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/materials", new { items = new[] { new { name = "Tiles", amount = 100m } } }, rami.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        (await (await Client.PostJsonAsync($"/api/employees/{rami.User.Id}/pay",
                new { basis = "hourly", amount = 8m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.FixedSalaryPaidMonthly)).Message.ShouldContain("Execution staff");
        await (await Client.PostJsonAsync("/api/invitations", new
            {
                email = $"site-{Guid.NewGuid():N}@example.test",
                role = RoleCodes.Employee,
                domainCode = DomainCodes.Execution,
                pay = new { basis = "hourly", amount = 8m, currency = "USD" },
            }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.FixedSalaryPaidMonthly);
    }
}
