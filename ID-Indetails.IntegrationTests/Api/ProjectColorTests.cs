using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Project colours: the thirty-colour palette each business uses on its own, the picker's list of who has which, and
/// swapping colours between two projects.
/// </summary>
public class ProjectColorTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<List<ProjectColorResponse>> ColorsAsync(string token, string domain)
    {
        var response = await Client.GetAsync($"/api/projects/colors?domain={domain}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<ProjectColorResponse>>();
    }

    private async Task<ProjectResponse> CreateAsync(string token, string domain, string title, string? tint = null)
    {
        var body = domain == DomainCodes.Execution
            ? (object)new { title, domainCode = domain, clientId = (await new ProjectTestSteps(_steps).CreateClientAsync(token, domain)).Id, tint }
            : new { title, domainCode = domain, billingType = "internal", tint };
        var response = await Client.PostJsonAsync("/api/projects", body, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    [Fact]
    public async Task The_picker_lists_the_thirty_colours_with_the_business_project_that_has_each()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var design = await CreateAsync(superAdmin.AccessToken, DomainCodes.Design, "Colour holder", "#2A7F62");

        var colors = await ColorsAsync(superAdmin.AccessToken, DomainCodes.Design);
        colors.Select(c => c.Hex).ShouldBe(ProjectColorRules.Palette.Select(c => c.Hex));
        colors.Single(c => c.Hex == "#2A7F62").TakenBy.ShouldBe(new ProjectCodeHolderResponse(design.Id, "Colour holder"));
        colors.Single(c => c.Hex == "#2A7F62").Name.ShouldBe("jade");

        // each business on its own: a Design project's colour is still free for Execution
        (await ColorsAsync(superAdmin.AccessToken, DomainCodes.Execution)).Single(c => c.Hex == "#2A7F62").TakenBy.ShouldBeNull();
    }

    [Fact]
    public async Task A_new_project_gets_a_colour_its_business_has_free()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var taken = (await ColorsAsync(superAdmin.AccessToken, DomainCodes.Design)).Where(c => c.TakenBy is not null).Select(c => c.Hex).ToHashSet();
        if (taken.Count >= ProjectColorRules.Palette.Count)
        {
            return;
        }

        var project = await CreateAsync(superAdmin.AccessToken, DomainCodes.Design, "Fresh colour");
        project.Tint.ShouldNotBeNull();
        ProjectColorRules.Palette.Select(c => c.Hex).ShouldContain(project.Tint);
        taken.ShouldNotContain(project.Tint);
    }

    [Fact]
    public async Task Two_projects_of_a_business_swap_colours_at_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var a = await CreateAsync(superAdmin.AccessToken, DomainCodes.Design, "Swap A", "#A53860");
        var b = await CreateAsync(superAdmin.AccessToken, DomainCodes.Design, "Swap B", "#1D2D44");

        var swapped = await (await Client.PostJsonAsync($"/api/projects/{a.Id}/swap-color", new { otherProjectId = b.Id }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        swapped.Tint.ShouldBe("#1D2D44");
        var other = await (await Client.GetAsync($"/api/projects/{b.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        other.Tint.ShouldBe("#A53860");
    }

    [Fact]
    public async Task A_swap_stays_within_a_business_and_needs_another_project_and_the_right_to_edit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var design = await CreateAsync(superAdmin.AccessToken, DomainCodes.Design, "Design side", "#A53860");
        var execution = await CreateAsync(superAdmin.AccessToken, DomainCodes.Execution, "Execution side", "#1D2D44");

        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/swap-color", new { otherProjectId = execution.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/swap-color", new { otherProjectId = design.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/swap-color", new { otherProjectId = execution.Id }, designer.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/colors?domain={DomainCodes.Design}", designer.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
