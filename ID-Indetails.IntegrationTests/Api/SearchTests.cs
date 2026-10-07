using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Search;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SearchTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private async Task<SearchResponse> SearchAsync(string q, string token)
    {
        var response = await Client.GetAsync($"/api/search?q={Uri.EscapeDataString(q)}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<SearchResponse>();
    }

    private async Task<T> CreatedAsync<T>(string url, object body, string token)
    {
        var response = await Client.PostJsonAsync(url, body, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Search_finds_only_what_the_caller_may_list()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var word = Unique("Zephyr");

        var client = await CreatedAsync<ClientResponse>("/api/clients", new
        {
            name = $"{word} Holdings",
            label = "Retail",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("client") } },
        }, admin.AccessToken);
        var project = await CreatedAsync<ProjectResponse>("/api/projects",
            new { title = $"{word} rebrand", domainCode = DomainCodes.Design, clientId = client.Id }, admin.AccessToken);
        var siteClient = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var site = await CreatedAsync<ProjectResponse>("/api/projects",
            new { title = $"{word} villa", domainCode = DomainCodes.Execution, clientId = siteClient.Id, projectFee = 1000m, materialMarkup = 0.1m },
            superAdmin.AccessToken);
        await _steps.ScalarAsync<int>("UPDATE users SET position = @p WHERE id = @id; SELECT 1", ("@p", $"{word} designer"), ("@id", employee.User.Id));

        var forAdmin = await SearchAsync(word, admin.AccessToken);
        forAdmin.Projects.Select(p => p.Id).ShouldBe([project.Id]);
        forAdmin.Clients.ShouldBe([new SearchClientResponse(client.Id, DomainCodes.Design, $"{word} Holdings", "Retail")]);
        forAdmin.People.ShouldHaveSingleItem().Id.ShouldBe(employee.User.Id);
        forAdmin.People[0].Position.ShouldBe($"{word} designer");

        // The client's label is searched too; every word must match.
        (await SearchAsync($"retail {word}", admin.AccessToken)).Clients.ShouldHaveSingleItem().Id.ShouldBe(client.Id);
        (await SearchAsync($"{word} nothing", admin.AccessToken)).Projects.ShouldBeEmpty();

        // Employees list projects only; the other domain's admin sees its own.
        var forEmployee = await SearchAsync(word, employee.AccessToken);
        forEmployee.Projects.Select(p => p.Id).ShouldBe([project.Id]);
        forEmployee.Clients.ShouldBeEmpty();
        forEmployee.People.ShouldBeEmpty();
        var forExecution = await SearchAsync(word, executionAdmin.AccessToken);
        forExecution.Projects.Select(p => p.Id).ShouldBe([site.Id]);
        forExecution.Clients.ShouldBeEmpty();
        forExecution.People.ShouldBeEmpty();
        (await SearchAsync(word, superAdmin.AccessToken)).Projects.Select(p => p.Id).ShouldBe([project.Id, site.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Search_returns_twelve_results_at_most_projects_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var word = Unique("Batch");
        for (var i = 0; i < 13; i++)
        {
            await CreatedAsync<ProjectResponse>("/api/projects", new { title = $"{word} sheet {i}", billingType = "internal" }, admin.AccessToken);
        }

        await CreatedAsync<ClientResponse>("/api/clients", new
        {
            name = $"{word} Studio",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("client") } },
        }, admin.AccessToken);

        var capped = await SearchAsync(word, admin.AccessToken);
        capped.Projects.Count.ShouldBe(12);
        capped.Clients.ShouldBeEmpty();
        capped.People.ShouldBeEmpty();

        // Nothing typed lists everything, in the same order and cap.
        (await SearchAsync("", admin.AccessToken)).Projects.Count.ShouldBe(12);
        (await SearchAsync("  ", admin.AccessToken)).Projects.Count.ShouldBe(12);
        await (await Client.GetAsync($"/api/search?q={new string('a', 101)}", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/search?q=x")).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }
}
