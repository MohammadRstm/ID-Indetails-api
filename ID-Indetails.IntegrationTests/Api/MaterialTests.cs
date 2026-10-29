using ID_Indetails.api.Business.Materials;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Materials;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class MaterialTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Site_staff_record_materials_and_see_costs_but_not_client_prices()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.MaterialWorkerAsync(superAdmin);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        var added = await AddAsync(project.Id, employee.AccessToken,
            new { name = "Rebar 12mm", quantity = 12m, unit = "tonne", unitCost = 1500m, supplier = "Al-Rashid Steel" },
            new { name = "  ceiling grid ", amount = 3200m },
            new { name = "Gypsum board", quantity = 10m, unitCost = 2m, amount = 25m });

        added.Count.ShouldBe(3);
        added[0].Cost.ShouldBe(18_000m);
        added[1].Name.ShouldBe("ceiling grid");
        added[2].UnitCost.ShouldBe(2.5m);
        added[2].Cost.ShouldBe(25m);
        added.ShouldAllBe(m => m.ClientPrice == null && m.PriceSource == null && m.CanEdit);

        var asEmployee = await ListAsync(project.Id, employee.AccessToken);
        asEmployee.All.ShouldBe(new MaterialTotalsResponse(3, 21_225m, null));
        asEmployee.DefaultMarkup.ShouldBeNull();
        asEmployee.CanAdd.ShouldBeTrue();

        var asAdmin = await ListAsync(project.Id, admin.AccessToken);
        asAdmin.All.Charge.ShouldBe(23_347.50m);
        asAdmin.Items.First(m => m.Name == "Rebar 12mm").ShouldSatisfyAllConditions(
            m => m.ClientPrice.ShouldBe(19_800m),
            m => m.PriceSource.ShouldBe(MaterialRules.PriceSource.Auto),
            m => m.Markup.ShouldBe(0.10m));

        // their cost counts straight away; their client prices are never revenue: the work price is in the BOQs
        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.Cost.Materials.ShouldBe(21_225m);
        money.Revenue.ShouldBe(1000m);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'material_added'", ("@p", project.Id))).ShouldBe(3);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'materials_added' AND recipient_id = @r",
            ("@p", project.Id), ("@r", admin.User.Id))).ShouldBe(1);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'materials_added' AND recipient_id = @r",
            ("@p", project.Id), ("@r", superAdmin.User.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task People_change_their_own_materials_and_managers_anyone_s()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, first) = await _steps.MaterialWorkerAsync(superAdmin);
        var (_, _, second) = await _steps.MaterialWorkerAsync(superAdmin);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        var mine = (await AddAsync(project.Id, first.AccessToken, new { name = "Cement", quantity = 40m, unit = "bag", unitCost = 7.5m }))[0];

        var others = await ListAsync(project.Id, second.AccessToken);
        others.Items.Single().CanEdit.ShouldBeFalse();
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{mine.Id}", new { name = "Cement", amount = 1m }, second.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/materials/{mine.Id}", second.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var edited = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{mine.Id}",
            new { name = "Cement", quantity = 50m, unit = "bag", unitCost = 7.5m, note = "Second pallet" }, first.AccessToken)).ReadAsync<MaterialResponse>();
        edited.Cost.ShouldBe(375m);
        edited.Note.ShouldBe("Second pallet");

        (await Client.DeleteAsync($"/api/projects/{project.Id}/materials/{mine.Id}", admin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await ListAsync(project.Id, admin.AccessToken)).All.Count.ShouldBe(0);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM project_materials WHERE id = @m AND deleted_at IS NOT NULL", ("@m", mine.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Client_prices_need_the_pricing_permission_and_follow_the_markup_unless_set()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.MaterialWorkerAsync(superAdmin);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/materials",
                new { items = new[] { new { name = "Tiles", amount = 100m, clientPrice = 150m } } }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var added = await AddAsync(project.Id, superAdmin.AccessToken,
            new { name = "Tiles", amount = 100m, clientPrice = (decimal?)110m },
            new { name = "Doors", amount = 200m, clientPrice = (decimal?)260m });
        added[0].PriceSource.ShouldBe(MaterialRules.PriceSource.Auto);
        added[1].PriceSource.ShouldBe(MaterialRules.PriceSource.Manual);

        var priced = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{added[0].Id}/client-price",
            new { clientPrice = 130m }, superAdmin.AccessToken)).ReadAsync<MaterialResponse>();
        priced.ClientPrice.ShouldBe(130m);
        priced.Markup.ShouldBe(0.30m);

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{added[0].Id}/client-price",
            new { clientPrice = 1m }, employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await Client.PutJsonAsync($"/api/projects/{project.Id}/pricing", new { materialMarkup = 0.20m, description = "Priced" }, superAdmin.AccessToken);
        var reset = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{added[1].Id}/client-price",
            new { clientPrice = (decimal?)null }, superAdmin.AccessToken)).ReadAsync<MaterialResponse>();
        reset.ClientPrice.ShouldBe(240m);
        reset.PriceSource.ShouldBe(MaterialRules.PriceSource.Auto);

        (await ListAsync(project.Id, superAdmin.AccessToken)).All.Charge.ShouldBe(370m);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'material_priced'", ("@p", project.Id))).ShouldBe(2);
    }

    [Fact]
    public async Task A_finalised_site_takes_no_material_changes_until_reopened_admins_included()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var material = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Sand", amount = 80m }))[0];

        // a material is a cost: completing doesn't wait for anything to be sent to the client
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/materials", new { items = new[] { new { name = "Late", amount = 5m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{material.Id}", new { name = "Sand", amount = 90m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{material.Id}/client-price", new { clientPrice = 1m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/materials/{material.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);

        var locked = await ListAsync(project.Id, superAdmin.AccessToken);
        locked.CanAdd.ShouldBeFalse();
        locked.Items.Single().CanEdit.ShouldBeFalse();

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/reopen", superAdmin.AccessToken);
        (await ListAsync(project.Id, superAdmin.AccessToken)).CanAdd.ShouldBeTrue();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/materials", new { items = new[] { new { name = "Late", amount = 5m } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_material_can_change_and_be_removed_while_the_site_is_open_whatever_the_client_was_sent()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 0m);
        var material = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Paint", quantity = 10m, unit = "litre", unitCost = 5m }))[0];

        // a client BOQ agreed and sent doesn't freeze the site's costs: they are plain costs
        var boq = await new BoqTestSteps(_steps).AgreeClientBoqAsync(superAdmin.AccessToken, project.Id, 500m);
        (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/deliveries", new { channel = "download" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var changed = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{material.Id}",
            new { name = "Paint", quantity = 12m, unit = "litre", unitCost = 5m, supplier = "Levant Paints" }, superAdmin.AccessToken)).ReadAsync<MaterialResponse>();
        changed.ShouldSatisfyAllConditions(
            m => m.Cost.ShouldBe(60m),
            m => m.Supplier.ShouldBe("Levant Paints"),
            m => m.PriceSource.ShouldBe(MaterialRules.PriceSource.Auto));
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{material.Id}/client-price", new { clientPrice = 99m }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.DeleteAsync($"/api/projects/{project.Id}/materials/{material.Id}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await ListAsync(project.Id, superAdmin.AccessToken)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Materials_are_filtered_grouped_and_paged()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await AddAsync(project.Id, superAdmin.AccessToken,
            new { name = "Cement", amount = 10m, supplier = "Beton Express" },
            new { name = "cement ", amount = 20m, supplier = "beton express" },
            new { name = "Rebar", amount = 30m, supplier = "Al-Rashid" });

        var all = await ListAsync(project.Id, superAdmin.AccessToken);
        all.Types.Select(t => (t.Key, t.Count, t.Cost)).ShouldBe([("cement", 2, 30m), ("rebar", 1, 30m)]);
        all.Suppliers.Select(s => s.Key).ShouldBe(["al-rashid", "beton express"]);

        var cement = await ListAsync(project.Id, superAdmin.AccessToken, "?type=cement&pageSize=1&page=2");
        cement.TotalCount.ShouldBe(2);
        cement.Items.Count.ShouldBe(1);
        cement.Matching.Cost.ShouldBe(30m);
        cement.All.Cost.ShouldBe(60m);

        (await ListAsync(project.Id, superAdmin.AccessToken, "?search=rashid")).TotalCount.ShouldBe(1);
        (await ListAsync(project.Id, superAdmin.AccessToken, "?supplier=beton%20express")).TotalCount.ShouldBe(2);
        (await ListAsync(project.Id, superAdmin.AccessToken, "?from=2000-01-01&to=2000-01-02")).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Other_domains_never_see_site_materials_and_design_projects_have_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var material = (await AddAsync(site.Id, superAdmin.AccessToken, new { name = "Gravel", amount = 50m }))[0];

        await (await Client.GetAsync($"/api/projects/{site.Id}/materials", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        // Material permissions belong to Execution, so a Design admin is refused before any lookup.
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/materials/{material.Id}", new { name = "X", amount = 1m }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{design.Id}/materials", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/materials", new { items = new[] { new { name = "X", amount = 1m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    private async Task<IReadOnlyList<MaterialResponse>> AddAsync(long projectId, string accessToken, params object[] items)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/materials", new { items }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<MaterialResponse>>();
    }

    private async Task<MaterialListResponse> ListAsync(long projectId, string accessToken, string query = "")
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/materials{query}", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MaterialListResponse>();
    }
}
