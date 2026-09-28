using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Projects;
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

    /// <summary>A Design client project with the given revenue, or an Execution site with a fee of 1000 and a 10% markup.</summary>
    public async Task<ProjectResponse> CreateProjectAsync(string accessToken, string domain, decimal revenue = 5000)
    {
        var client = await CreateClientAsync(accessToken, domain);
        object body = domain == DomainCodes.Execution
            ? new { title = "Site " + Guid.NewGuid().ToString("N")[..8], domainCode = domain, clientId = client.Id, projectFee = 1000m, materialMarkup = 0.10m }
            : new { title = "Project " + Guid.NewGuid().ToString("N")[..8], domainCode = domain, clientId = client.Id, revenue };

        var response = await Client.PostJsonAsync("/api/projects", body, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }
}
