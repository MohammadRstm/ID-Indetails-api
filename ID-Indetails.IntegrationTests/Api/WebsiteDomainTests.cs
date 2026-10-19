using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Website;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The Website as a domain of its own: its staff, and anyone given its permissions (docs/website.md).</summary>
public class WebsiteDomainTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private ProjectTestSteps Projects => new(_steps);
    private HttpClient Client => _steps.Client;

    private async Task<CurrentUserResponse> MeAsync(AuthResponse who)
        => await (await Client.GetAsync("/api/auth/me", who.AccessToken)).ReadAsync<CurrentUserResponse>();

    private async Task<List<WebsiteLinkCandidateResponse>> CandidatesAsync(AuthResponse who, string search)
    {
        var response = await Client.GetAsync($"/api/website/link-candidates?search={search}", who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<WebsiteLinkCandidateResponse>>();
    }

    [Fact]
    public async Task Website_staff_are_admins_who_reach_only_the_website()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.PostJsonAsync("/api/invitations",
                new { email = $"web-{Guid.NewGuid():N}@example.com", role = RoleCodes.Employee, domainCode = DomainCodes.Website }, superAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.InvitationNotAllowed);
        var (_, _, staff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Website);

        var me = await MeAsync(staff);
        me.Domain.ShouldBe(DomainCodes.Website);
        me.VisibleDomains.ShouldBe([DomainCodes.Website]);
        me.Permissions.ShouldBe([PermissionCodes.WebsiteManage, PermissionCodes.WebsiteView], ignoreOrder: true);

        (await Client.GetAsync("/api/website/projects", staff.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.GetAsync("/api/projects", staff.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        // they type their projects in: the businesses' projects aren't theirs to search
        (await CandidatesAsync(staff, design.Title)).ShouldBeEmpty();
        (await CandidatesAsync(superAdmin, design.Title)).Single().Id.ShouldBe(design.Id);

        // the businesses' pages have no website version
        await (await Client.GetAsync($"/api/dashboard/{DomainCodes.Website}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/domain-settings/{DomainCodes.Website}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_design_admin_given_the_website_reaches_it_and_still_works_in_design_alone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var execution = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        var grant = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WebsiteManage, action = "grant", reason = "Runs the website" } } },
            superAdmin.AccessToken);
        grant.IsSuccessStatusCode.ShouldBeTrue(await grant.Content.ReadAsStringAsync());

        (await MeAsync(admin)).VisibleDomains.ShouldBe([DomainCodes.Design, DomainCodes.Website]);
        // still only one business: a new project goes to Design without asking
        var created = await Client.PostJsonAsync("/api/projects", new { title = $"Project {Guid.NewGuid():N}"[..20], billingType = "internal" }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        (await CandidatesAsync(admin, execution.Title)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Website_staff_are_paid_as_a_company_cost()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"/api/company/overview?from={today:yyyy-MM}-01&to={today:yyyy-MM-dd}";
        var before = await (await Client.GetAsync(url, superAdmin.AccessToken)).ReadAsync<CompanyOverviewResponse>();

        await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Website);

        var after = await (await Client.GetAsync(url, superAdmin.AccessToken)).ReadAsync<CompanyOverviewResponse>();
        after.NetProfit.ShouldBeLessThan(before.NetProfit);
        after.Domains.Select(d => d.Domain).ShouldBe(DomainCodes.Businesses);
    }
}
