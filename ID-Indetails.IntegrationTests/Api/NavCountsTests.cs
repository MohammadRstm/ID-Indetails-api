using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Search;
using ID_Indetails.api.DTO.Response.Auth;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The sidebar's counts in one call: each the total its list would give, and only for lists the caller may see.</summary>
public class NavCountsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private sealed record Total(int TotalCount);

    private async Task<T> OkAsync<T>(string url, AuthResponse who)
    {
        var response = await Client.GetAsync(url, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task The_counts_are_the_lists_totals_and_only_for_lists_the_caller_may_see()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        foreach (var domain in new[] { DomainCodes.Design, DomainCodes.Execution })
        {
            var counts = await OkAsync<NavCountsResponse>($"/api/search/counts?domain={domain}", superAdmin);
            counts.Projects.ShouldBe((await OkAsync<Total>($"/api/projects?domain={domain}&status=active&pageSize=1", superAdmin)).TotalCount);
            counts.Clients.ShouldBe((await OkAsync<Total>($"/api/clients?domain={domain}&pageSize=1", superAdmin)).TotalCount);
            counts.People.ShouldBe((await OkAsync<Total>($"/api/employees?domain={domain}&status=active&pageSize=1", superAdmin)).TotalCount);
            counts.Projects.ShouldBe(1);
        }

        // an employee lists projects only
        var theirs = await OkAsync<NavCountsResponse>($"/api/search/counts?domain={DomainCodes.Design}", employee);
        theirs.Projects.ShouldNotBeNull();
        theirs.Clients.ShouldBeNull();
        theirs.People.ShouldBeNull();
    }
}
