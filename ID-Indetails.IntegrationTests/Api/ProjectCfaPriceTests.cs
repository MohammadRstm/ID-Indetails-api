using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectCfaPriceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task The_CFA_price_is_a_reference_that_only_those_who_see_the_money_see_or_set()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // admins don't see project money until a super admin hands them financial:view
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        var created = await CreateAsync(superAdmin, new { title = "Dakar offices", billingType = "internal", domainCode = DomainCodes.Design, cfaPrice = 15_000_000m });
        created.Money!.CfaPrice.ShouldBe(15_000_000m);
        // in no calculation: the project's money stays in USD
        created.Money.Revenue.ShouldBe(0m);
        created.Money.Currency.ShouldBe("USD");

        // the admin neither sees it nor sets it
        (await GetAsync(admin, created.Id)).Money.ShouldBeNull();
        await (await Client.PutJsonAsync($"/api/projects/{created.Id}",
            new { title = created.Title, ownerId = created.Owner.Id, cfaPrice = 1m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Abidjan villa", billingType = "internal", domainCode = DomainCodes.Design, cfaPrice = 1m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // their form leaves it out, and it stays
        await (await Client.PutJsonAsync($"/api/projects/{created.Id}",
            new { title = "Dakar offices, phase 2", ownerId = created.Owner.Id }, admin.AccessToken)).ReadAsync<ProjectResponse>();
        (await GetAsync(superAdmin, created.Id)).Money!.CfaPrice.ShouldBe(15_000_000m);

        // whoever sees the money changes it, and 0 clears it
        var changed = await PutAsync(superAdmin, created, 16_500_000m);
        changed.Money!.CfaPrice.ShouldBe(16_500_000m);
        var cleared = await PutAsync(superAdmin, created, 0m);
        cleared.Money!.CfaPrice.ShouldBeNull();
    }

    [Fact]
    public async Task The_CFA_price_is_in_whole_francs()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Lomé shop", billingType = "internal", domainCode = DomainCodes.Design, cfaPrice = 1_500_000.5m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Lomé shop", billingType = "internal", domainCode = DomainCodes.Design, cfaPrice = -1m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // none given: none kept
        var none = await CreateAsync(superAdmin, new { title = "Lomé shop", billingType = "internal", domainCode = DomainCodes.Design });
        none.Money!.CfaPrice.ShouldBeNull();
    }

    private async Task<ProjectResponse> CreateAsync(ID_Indetails.api.DTO.Response.Auth.AuthResponse user, object body)
    {
        var response = await Client.PostJsonAsync("/api/projects", body, user.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private async Task<ProjectResponse> GetAsync(ID_Indetails.api.DTO.Response.Auth.AuthResponse user, long projectId)
        => await (await Client.GetAsync($"/api/projects/{projectId}", user.AccessToken)).ReadAsync<ProjectResponse>();

    private async Task<ProjectResponse> PutAsync(ID_Indetails.api.DTO.Response.Auth.AuthResponse user, ProjectResponse project, decimal cfaPrice)
        => await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = project.Title, ownerId = project.Owner.Id, cfaPrice }, user.AccessToken)).ReadAsync<ProjectResponse>();
}
