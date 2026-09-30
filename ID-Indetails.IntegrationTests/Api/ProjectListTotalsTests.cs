using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectListTotalsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<T> OkAsync<T>(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Totals_add_up_the_money_of_every_project_the_filters_match_whatever_the_page()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var client = await new ProjectTestSteps(_steps).CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        var created = await Client.PostJsonAsync("/api/projects", new { title = "Studio tooling", billingType = "internal" }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var tooling = await created.ReadAsync<ProjectResponse>();
        foreach (var (projectId, amount) in new[] { (client.Id, 500m), (tooling.Id, 200m) })
        {
            (await Client.PostJsonAsync($"/api/projects/{projectId}/expenses",
                new { label = "Prints", amount, currency = "USD", spentOn = "2026-06-15" }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        }

        // One project a page: the totals still cover both.
        var totals = await OkAsync<ProjectListTotalsResponse>("/api/projects/totals?domain=design&pageSize=1", admin.AccessToken);
        var rows = (await OkAsync<PagedResponse<ProjectSummaryResponse>>("/api/projects?domain=design&pageSize=100", admin.AccessToken)).Items;
        totals.Projects.ShouldBe(2);
        totals.ClientProjects.ShouldBe(1);
        totals.InternalProjects.ShouldBe(1);
        totals.Revenue.ShouldBe(10_000m);
        totals.Cost.ShouldBe(700m);
        totals.Profit.ShouldBe(rows.Single(r => r.Id == client.Id).Money!.Profit);
        totals.InternalCost.ShouldBe(200m);
        totals.HasUnconverted.ShouldBeFalse();

        var inHouse = await OkAsync<ProjectListTotalsResponse>("/api/projects/totals?billingType=internal", admin.AccessToken);
        inHouse.Projects.ShouldBe(1);
        inHouse.Revenue.ShouldBe(0m);

        await (await Client.GetAsync("/api/projects/totals", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // The Financials export has the same projects, then the period's totals.
        var export = await Client.GetAsync("/api/finance/design/projects/export?from=2026-01-01&to=2026-06-30", admin.AccessToken);
        ((int)export.StatusCode).ShouldBe(200, await export.Content.ReadAsStringAsync());
        export.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var csv = await export.Content.ReadAsStringAsync();
        csv.ShouldContain("Project,Client,Status,Billing,Revenue,Currency,Hours,Labour cost,Expenses,Materials,Ground labour,Total cost,Profit,Margin %");
        csv.ShouldContain("Studio tooling,Internal,active,Internal,0.00,USD,0.00,0.00,200.00,0.00,0.00,200.00,");
        csv.ShouldContain("Period,2026-01-01 to 2026-06-30");
        await (await Client.GetAsync("/api/finance/design/projects/export", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
