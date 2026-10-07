using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Statements;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Reusable steps for tests that need clients and projects.</summary>
public sealed class ProjectTestSteps(AuthTestSteps steps)
{
    private HttpClient Client => steps.Client;

    public async Task<ClientResponse> CreateClientAsync(string accessToken, string domain)
    {
        var response = await Client.PostJsonAsync("/api/clients", new
        {
            name = "Client " + Guid.NewGuid().ToString("N")[..8],
            label = "Client",
            domainCode = domain,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("client") } },
        }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ClientResponse>();
    }

    /// <summary>
    /// A Design client project with the given revenue, or an Execution site with a fee of 1000 and a 10% markup. A caller
    /// who may not set that money (a plain admin) gets the project without it: no revenue, or no fee and no markup.
    /// </summary>
    public async Task<ProjectResponse> CreateProjectAsync(string accessToken, string domain, decimal revenue = 5000)
    {
        var client = await CreateClientAsync(accessToken, domain);
        var me = await (await Client.GetAsync("/api/auth/me", accessToken)).ReadAsync<CurrentUserResponse>();
        var title = (domain == DomainCodes.Execution ? "Site " : "Project ") + Guid.NewGuid().ToString("N")[..8];
        object body = domain == DomainCodes.Execution
            ? me.Permissions.Contains(PermissionCodes.ProjectPricingManage)
                ? new { title, domainCode = domain, clientId = client.Id, projectFee = 1000m, materialMarkup = 0.10m }
                : new { title, domainCode = domain, clientId = client.Id }
            : me.Permissions.Contains(PermissionCodes.FinancialView)
                ? new { title, domainCode = domain, clientId = client.Id, revenue }
                : new { title, domainCode = domain, clientId = client.Id };

        var response = await Client.PostJsonAsync("/api/projects", body, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    /// <summary>Billing settings with 45-day terms, prefix HL for Design and EX for Execution.</summary>
    public async Task SetUpBillingAsync(string accessToken, string domain)
    {
        var response = await Client.PutJsonAsync($"/api/billing-settings/{domain}", new
        {
            invoicePrefix = domain == DomainCodes.Execution ? "EX" : "HL",
            defaultTermsDays = 45,
        }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A detailed statement of the site's new materials, sent (saved as a download): from then their client prices are
    /// revenue. Returns the statement's id.
    /// </summary>
    public async Task<long> SendStatementAsync(string accessToken, long projectId)
    {
        var made = await Client.PostJsonAsync($"/api/projects/{projectId}/statements", new { mode = "detail" }, accessToken);
        ((int)made.StatusCode).ShouldBe(201, await made.Content.ReadAsStringAsync());
        var statement = await made.ReadAsync<StatementResponse>();
        var sent = await Client.PostJsonAsync($"/api/statements/{statement.Id}/deliveries", new { channel = "download" }, accessToken);
        ((int)sent.StatusCode).ShouldBe(200, await sent.Content.ReadAsStringAsync());
        return statement.Id!.Value;
    }
}
