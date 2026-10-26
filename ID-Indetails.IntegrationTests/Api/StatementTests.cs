using System.Text.RegularExpressions;
using ID_Indetails.api.Business.Materials;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Materials;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class StatementTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task A_statement_shows_the_chosen_materials_and_freezes_their_prices()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        var materials = await AddMaterialsAsync(project.Id, admin.AccessToken,
            new { name = "Cement", quantity = 40m, unit = "bag", unitCost = 7.5m },
            new { name = "Tiles", amount = 1000m });
        await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 500m, currency = "USD" }, admin.AccessToken);

        var preview = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements/preview",
            new { mode = "detail", excludedMaterialIds = new[] { materials[1].Id } }, admin.AccessToken, 200);
        preview.IsPreview.ShouldBeTrue();
        preview.Id.ShouldBeNull();

        var statement = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements",
            new { mode = "detail", excludedMaterialIds = new[] { materials[1].Id } }, admin.AccessToken, 201);

        statement.Number.ShouldBe(preview.Number);
        Regex.IsMatch(statement.Number, @"^ST-\d{4}-\d{3,}$").ShouldBeTrue();
        statement.Types.ShouldBe([new StatementTypeResponse("cement", "Cement", "40 bag", 1, 0, 330m, 330m)]);
        statement.ShouldSatisfyAllConditions(
            s => s.Total.ShouldBe(330m),
            s => s.Received.ShouldBe(500m),
            // the fee and the materials on it: the tiles left out aren't owed until a statement sends them
            s => s.Agreed.ShouldBe(1330m),
            s => s.Remaining.ShouldBe(830m),
            s => s.Since.ShouldBeNull(),
            s => s.CreatedBy!.Id.ShouldBe(admin.User.Id));

        // A markup change reprices the material left out, never the stated one.
        await Client.PutJsonAsync($"/api/projects/{project.Id}/pricing", new { projectFee = 1000m, materialMarkup = 0.50m, description = "Priced" }, admin.AccessToken);
        var list = await (await Client.GetAsync($"/api/projects/{project.Id}/materials", admin.AccessToken)).ReadAsync<MaterialListResponse>();
        list.Items.Single(m => m.Id == materials[0].Id).ShouldSatisfyAllConditions(
            m => m.ClientPrice.ShouldBe(330m),
            m => m.PriceSource.ShouldBe(MaterialRules.PriceSource.Stated));
        list.Items.Single(m => m.Id == materials[1].Id).ClientPrice.ShouldBe(1500m);

        // made but not sent yet: nothing is revenue; sending it adds the cement at its stated price
        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.MaterialCharge.ShouldBe(0m);
        money.UnsentMaterialCharge.ShouldBe(1830m);
        await PostAsync<StatementResponse>($"/api/statements/{statement.Id}/deliveries", new { channel = "download" }, admin.AccessToken, 200);
        money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.MaterialCharge.ShouldBe(330m);
        money.UnsentMaterialCharge.ShouldBe(1500m);
        money.Revenue.ShouldBe(1330m);

        var again = await (await Client.GetAsync($"/api/statements/{statement.Id}", admin.AccessToken)).ReadAsync<StatementResponse>();
        again.Agreed.ShouldBe(1330m);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'statement_created'", ("@p", project.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Later_statements_take_what_is_new_and_can_bring_earlier_types_back()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await AddMaterialsAsync(project.Id, superAdmin.AccessToken, new { name = "Cement", amount = 100m }, new { name = "Rebar", amount = 200m });
        var first = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements", new { mode = "detail" }, superAdmin.AccessToken, 201);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/statements", new { mode = "detail" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.StatementEmpty);

        await AddMaterialsAsync(project.Id, superAdmin.AccessToken, new { name = "cement", amount = 50m });
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/statements",
                new { mode = "detail", includeEarlier = true, unlistedTypeKeys = new[] { "cement" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var second = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements",
            new { mode = "detail", includeEarlier = true, unlistedTypeKeys = new[] { "rebar" } }, superAdmin.AccessToken, 201);

        second.Since.ShouldBe(first.Date);
        second.Types.ShouldBe([new StatementTypeResponse("cement", "Cement", null, 2, 110m, 55m, 165m)]);
        second.HiddenTypes.ShouldBe(new StatementHiddenTypesResponse(1, 220m));
        second.Total.ShouldBe(385m);
        second.Number.ShouldNotBe(first.Number);

        var total = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements",
            new { mode = "total", includeEarlier = true }, superAdmin.AccessToken, 201);
        total.Types.ShouldBeEmpty();
        total.Total.ShouldBe(385m);

        var page = await (await Client.GetAsync($"/api/projects/{project.Id}/statements?pageSize=2", superAdmin.AccessToken))
            .ReadAsync<PagedResponse<StatementSummaryResponse>>();
        page.TotalCount.ShouldBe(3);
        page.Items.Select(s => s.Number).ShouldBe([total.Number, second.Number]);
    }

    [Fact]
    public async Task Sends_are_recorded_by_people_who_may_send_statements()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await AddMaterialsAsync(project.Id, superAdmin.AccessToken, new { name = "Sand", amount = 80m });
        var statement = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements", new { mode = "total" }, superAdmin.AccessToken, 201);

        await (await Client.PostJsonAsync($"/api/statements/{statement.Id}/deliveries", new { channel = "email", recipient = "x@test.local" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/statements/{statement.Id}", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/statements", new { mode = "total" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await PostAsync<StatementResponse>($"/api/statements/{statement.Id}/deliveries", new { channel = "download" }, superAdmin.AccessToken, 200);
        var sent = await PostAsync<StatementResponse>($"/api/statements/{statement.Id}/deliveries",
            new { channel = "whatsapp", recipient = "+961 3 555 730" }, superAdmin.AccessToken, 200);
        sent.Deliveries.Select(d => d.Channel).ShouldBe([DeliveryChannel.Download, DeliveryChannel.Whatsapp]);

        var list = await (await Client.GetAsync($"/api/projects/{project.Id}/statements", superAdmin.AccessToken))
            .ReadAsync<PagedResponse<StatementSummaryResponse>>();
        list.Items.Single().ShouldSatisfyAllConditions(
            s => s.DeliveryCount.ShouldBe(2),
            s => s.LastDelivery!.Channel.ShouldBe(DeliveryChannel.Whatsapp));
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'statement_sent'", ("@p", project.Id))).ShouldBe(2);
    }

    [Fact]
    public async Task Statements_can_be_made_for_a_finalised_site()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await AddMaterialsAsync(project.Id, superAdmin.AccessToken, new { name = "Glass", amount = 900m });
        // every material sent before completing, which freezes what is owed
        await Projects.SendStatementAsync(superAdmin.AccessToken, project.Id);
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var statement = await PostAsync<StatementResponse>($"/api/projects/{project.Id}/statements", new { mode = "detail", includeEarlier = true }, superAdmin.AccessToken, 201);

        statement.Total.ShouldBe(990m);
        statement.Agreed.ShouldBe(1990m);
    }

    [Fact]
    public async Task Other_domains_never_see_statements_and_design_projects_have_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await AddMaterialsAsync(site.Id, superAdmin.AccessToken, new { name = "Gravel", amount = 50m });
        var statement = await PostAsync<StatementResponse>($"/api/projects/{site.Id}/statements", new { mode = "detail" }, superAdmin.AccessToken, 201);

        // A design admin granted the money gets past the permission check and must meet a 404.
        await (await Client.GetAsync($"/api/statements/{statement.Id}", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{site.Id}/statements", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/projects/{design.Id}/statements", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    private async Task<IReadOnlyList<MaterialResponse>> AddMaterialsAsync(long projectId, string accessToken, params object[] items)
        => await PostAsync<List<MaterialResponse>>($"/api/projects/{projectId}/materials", new { items }, accessToken, 201);

    private async Task<T> PostAsync<T>(string url, object body, string accessToken, int status)
    {
        var response = await Client.PostJsonAsync(url, body, accessToken);
        ((int)response.StatusCode).ShouldBe(status, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }
}
