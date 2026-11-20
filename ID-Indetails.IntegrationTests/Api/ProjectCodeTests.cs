using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Search;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectCodeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task A_new_project_gets_the_next_code_of_its_quarter_in_its_domain_and_its_names_initials()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var design = await NextAsync(superAdmin, DomainCodes.Design);
        var execution = await NextAsync(superAdmin, DomainCodes.Execution);

        var villa = await CreateAsync(superAdmin, DomainCodes.Design, new { title = "Villa Kadmous", billingType = "internal", domainCode = DomainCodes.Design });
        villa.ProjectCode.ShouldBe(design.ProjectCode);
        villa.ProjectCode.ShouldStartWith(ProjectCodeRules.PeriodOf(villa.StartDate));
        villa.BuildingCode.ShouldBe("VK");
        villa.Code.ShouldBe($"{villa.ProjectCode}-VK");

        // each domain counts on its own
        (await NextAsync(superAdmin, DomainCodes.Design)).ProjectCode.ShouldBe(Bump(villa.ProjectCode));
        (await NextAsync(superAdmin, DomainCodes.Execution)).ProjectCode.ShouldBe(execution.ProjectCode);

        var list = await (await Client.GetAsync($"/api/projects?search={villa.ProjectCode}", superAdmin.AccessToken))
            .ReadAsync<PagedResponse<ProjectSummaryResponse>>();
        list.Items.Single(p => p.Id == villa.Id).Code.ShouldBe(villa.Code);
    }

    [Fact]
    public async Task A_typed_code_is_kept_in_capitals_and_one_already_used_in_the_domain_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var code = $"{(await NextAsync(superAdmin, DomainCodes.Design)).ProjectCode[..6]}900";

        var first = await CreateAsync(superAdmin, DomainCodes.Design,
            new { title = "Harbour offices", billingType = "internal", domainCode = DomainCodes.Design, projectCode = code.ToLowerInvariant(), buildingCode = " ho3 " });
        first.ProjectCode.ShouldBe(code);
        first.BuildingCode.ShouldBe("HO3");
        // the next one follows the highest number of the quarter
        (await NextAsync(superAdmin, DomainCodes.Design)).ProjectCode.ShouldBe(Bump(code));

        var taken = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Another", billingType = "internal", domainCode = DomainCodes.Design, projectCode = code }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectCodeTaken);
        taken.Message.ShouldContain("Harbour offices");

        // the other domain numbers its own projects
        var site = await new ProjectTestSteps(_steps).CreateClientAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var sameCode = await CreateAsync(superAdmin, DomainCodes.Execution,
            new { title = "Harbour site", domainCode = DomainCodes.Execution, clientId = site.Id, projectCode = code });
        sameCode.ProjectCode.ShouldBe(code);

        var check = await NextAsync(superAdmin, DomainCodes.Design, $"&check={code.ToLowerInvariant()}");
        check.TakenBy.ShouldBe(new ProjectCodeHolderResponse(first.Id, "Harbour offices"));
        (await NextAsync(superAdmin, DomainCodes.Design, $"&check={code}&projectId={first.Id}")).TakenBy.ShouldBeNull();

        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Odd", billingType = "internal", domainCode = DomainCodes.Design, projectCode = "ID26Q5001" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Odd", billingType = "internal", domainCode = DomainCodes.Design, buildingCode = "V-K" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Codes_change_with_the_projects_details_and_stay_when_left_out()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await CreateAsync(superAdmin, DomainCodes.Design, new { title = "Achrafieh duplex", billingType = "internal", domainCode = DomainCodes.Design });
        var other = await CreateAsync(superAdmin, DomainCodes.Design, new { title = "Faqra villa", billingType = "internal", domainCode = DomainCodes.Design });

        // as the phone app saves the details: without the codes
        var renamed = await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = "Achrafieh duplex, phase 2", ownerId = project.Owner.Id }, superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        renamed.ProjectCode.ShouldBe(project.ProjectCode);
        renamed.BuildingCode.ShouldBe("AD");

        var code = Bump(Bump(other.ProjectCode));
        var recoded = await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = renamed.Title, ownerId = project.Owner.Id, projectCode = code, buildingCode = "ad2" }, superAdmin.AccessToken))
            .ReadAsync<ProjectResponse>();
        recoded.Code.ShouldBe($"{code}-AD2");

        // its own code is no clash; another project's is
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = renamed.Title, ownerId = project.Owner.Id, projectCode = code }, superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}",
            new { title = renamed.Title, ownerId = project.Owner.Id, projectCode = other.ProjectCode }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectCodeTaken);
    }

    [Fact]
    public async Task Projects_are_found_by_either_code_or_the_full_code()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await CreateAsync(superAdmin, DomainCodes.Design,
            new { title = "Bayt Capital", billingType = "internal", domainCode = DomainCodes.Design, buildingCode = "ZQX9" });

        foreach (var words in new[] { project.ProjectCode, "zqx9", project.Code })
        {
            var list = await (await Client.GetAsync($"/api/projects?search={Uri.EscapeDataString(words)}", superAdmin.AccessToken))
                .ReadAsync<PagedResponse<ProjectSummaryResponse>>();
            list.Items.Select(p => p.Id).ShouldContain(project.Id, words);
        }

        var search = await (await Client.GetAsync($"/api/search?q={Uri.EscapeDataString(project.Code)}", superAdmin.AccessToken))
            .ReadAsync<SearchResponse>();
        search.Projects.Single(p => p.Id == project.Id).Code.ShouldBe(project.Code);
    }

    [Fact]
    public async Task The_next_code_is_for_those_who_create_or_edit_projects_in_a_domain_they_see()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        (await NextAsync(admin, DomainCodes.Design)).ProjectCode.ShouldStartWith("ID");
        await (await Client.GetAsync("/api/projects/next-code?domain=execution", admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/projects/next-code?domain=design", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/projects/next-code?domain=website", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    private async Task<NextProjectCodeResponse> NextAsync(AuthResponse user, string domain, string more = "")
        => await (await Client.GetAsync($"/api/projects/next-code?domain={domain}{more}", user.AccessToken)).ReadAsync<NextProjectCodeResponse>();

    private async Task<ProjectResponse> CreateAsync(AuthResponse user, string domain, object body)
    {
        var response = await Client.PostJsonAsync("/api/projects", body, user.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var project = await response.ReadAsync<ProjectResponse>();
        project.Domain.ShouldBe(domain);
        return project;
    }

    /// <summary>The code after this one in its quarter: ID26Q4007 → ID26Q4008.</summary>
    private static string Bump(string code) => $"{code[..6]}{int.Parse(code[6..]) + 1:000}";
}
