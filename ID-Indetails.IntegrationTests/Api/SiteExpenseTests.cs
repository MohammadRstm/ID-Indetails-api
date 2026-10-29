using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>An Execution site's expenses: a cost only, with a day, a category and an optional account label.</summary>
public class SiteExpenseTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Site_staff_record_expenses_and_see_their_costs()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.MaterialWorkerAsync(superAdmin);
        // priced above the costs below, so the profit floor (tested on its own) stays out of the way
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution, projectFee: 30_000m);

        var added = await AddAsync(project.Id, employee.AccessToken,
            new { name = "Rebar 12mm", quantity = 12m, unit = "tonne", unitCost = 1500m, supplier = "Al-Rashid Steel" },
            new { name = "  ceiling grid ", amount = 3200m },
            new { name = "Gypsum board", quantity = 10m, unitCost = 2m, amount = 25m });

        added.Count.ShouldBe(3);
        added[0].Cost.ShouldBe(18_000m);
        added[1].Name.ShouldBe("ceiling grid");
        added[2].UnitCost.ShouldBe(2.5m);
        added[2].Cost.ShouldBe(25m);
        // left out, an expense is USD, spent today, in materials, under no label
        added.ShouldAllBe(e => e.CanEdit && e.Currency == "USD" && e.Category == SiteExpenseCategory.Materials && e.AccountLabel == null && e.SpentOn == e.AddedOn);

        var list = await ListAsync(project.Id, employee.AccessToken);
        list.All.Count.ShouldBe(3);
        list.All.Cost.ShouldBe(21_225m);
        list.All.ByCurrency.Select(t => (t.Currency, t.Total)).ShouldBe([("USD", 21_225m)]);
        list.CanAdd.ShouldBeTrue();

        // a cost counts straight away; nothing is charged to the client, so the revenue is the work price alone
        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.Cost.SiteExpenses.ShouldBe(21_225m);
        money.Cost.Expenses.ShouldBe(0m);
        money.Revenue.ShouldBe(30_000m);

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
    public async Task The_responses_have_no_client_prices_markup_or_charge()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Tiles", amount = 100m });

        var list = JsonDocument.Parse(await (await Client.GetAsync($"/api/projects/{project.Id}/site-expenses", superAdmin.AccessToken)).Content.ReadAsStringAsync()).RootElement;
        list.EnumerateObject().Select(p => p.Name).ShouldNotContain("defaultMarkup");
        list.GetProperty("all").EnumerateObject().Select(p => p.Name).ShouldNotContain("charge");
        var item = list.GetProperty("items")[0];
        item.EnumerateObject().Select(p => p.Name).ShouldBeSubsetOf(
        [
            "id", "name", "quantity", "unit", "unitCost", "cost", "currency", "supplier", "note", "spentOn", "category", "accountLabel",
            "addedAt", "addedOn", "addedBy", "updatedAt", "canEdit",
        ]);
        item.GetProperty("category").GetString().ShouldBe("materials");

        var money = JsonDocument.Parse(await (await Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken)).Content.ReadAsStringAsync())
            .RootElement.GetProperty("money");
        money.EnumerateObject().Select(p => p.Name).ShouldNotContain("materialMarkup");
        money.GetProperty("cost").EnumerateObject().Select(p => p.Name).ShouldContain("siteExpenses");
        money.GetProperty("cost").EnumerateObject().Select(p => p.Name).ShouldNotContain("materials");
    }

    [Fact]
    public async Task People_change_their_own_expenses_and_managers_anyone_s()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, first) = await _steps.MaterialWorkerAsync(superAdmin);
        var (_, _, second) = await _steps.MaterialWorkerAsync(superAdmin);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);
        var mine = (await AddAsync(project.Id, first.AccessToken, new { name = "Cement", quantity = 40m, unit = "bag", unitCost = 7.5m }))[0];

        var others = await ListAsync(project.Id, second.AccessToken);
        others.Items.Single().CanEdit.ShouldBeFalse();
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{mine.Id}", new { name = "Cement", amount = 1m }, second.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/site-expenses/{mine.Id}", second.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var edited = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{mine.Id}",
            new { name = "Cement", quantity = 50m, unit = "bag", unitCost = 7.5m, note = "Second pallet" }, first.AccessToken)).ReadAsync<SiteExpenseResponse>();
        edited.Cost.ShouldBe(375m);
        edited.Note.ShouldBe("Second pallet");

        (await Client.DeleteAsync($"/api/projects/{project.Id}/site-expenses/{mine.Id}", admin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await ListAsync(project.Id, admin.AccessToken)).All.Count.ShouldBe(0);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM site_expenses WHERE id = @m AND deleted_at IS NOT NULL", ("@m", mine.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task An_expense_has_a_day_a_category_a_currency_and_a_label_and_a_change_keeps_what_it_leaves_out()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // no work price agreed yet, so the profit floor (tested on its own) stays out of the way
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 0m);
        var label = await AddLabelAsync(superAdmin.AccessToken);
        await LbpRateAsync(superAdmin.AccessToken);

        var crane = (await AddAsync(project.Id, superAdmin.AccessToken, new
        {
            name = "Crane hire", amount = 900m, supplier = "Beirut Cranes", category = "contractor", currency = "lbp",
            spentOn = "2026-09-14", accountLabelId = label.Id,
        }))[0];
        crane.ShouldSatisfyAllConditions(
            e => e.Category.ShouldBe(SiteExpenseCategory.Contractor),
            e => e.Currency.ShouldBe("LBP"),
            e => e.SpentOn.ShouldBe(new DateOnly(2026, 9, 14)),
            e => e.Supplier.ShouldBe("Beirut Cranes"),
            e => e.AccountLabel!.Id.ShouldBe(label.Id),
            e => e.AccountLabel!.Name.ShouldBe(label.Name));

        // the cost is in its own currency: the list keeps one total per currency and never adds them together
        await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Sand", amount = 50m });
        var all = (await ListAsync(project.Id, superAdmin.AccessToken)).All;
        all.Count.ShouldBe(2);
        all.Cost.ShouldBe(50m);
        all.ByCurrency.Select(t => (t.Currency, t.Total)).ShouldBe([("LBP", 900m), ("USD", 50m)]);

        // a change leaves the currency, day and category as they are unless it names them; its label is what it says
        var renamed = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{crane.Id}",
            new { name = "Crane hire (2 days)", amount = 1800m }, superAdmin.AccessToken)).ReadAsync<SiteExpenseResponse>();
        renamed.ShouldSatisfyAllConditions(
            e => e.Currency.ShouldBe("LBP"),
            e => e.Category.ShouldBe(SiteExpenseCategory.Contractor),
            e => e.SpentOn.ShouldBe(new DateOnly(2026, 9, 14)),
            e => e.Cost.ShouldBe(1800m),
            e => e.AccountLabel.ShouldBeNull());

        var moved = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{crane.Id}",
            new { name = "Crane hire (2 days)", amount = 1800m, category = "equipment", spentOn = "2026-09-15", accountLabelId = label.Id, currency = "USD" },
            superAdmin.AccessToken)).ReadAsync<SiteExpenseResponse>();
        moved.ShouldSatisfyAllConditions(
            e => e.Currency.ShouldBe("USD"),
            e => e.Category.ShouldBe(SiteExpenseCategory.Equipment),
            e => e.SpentOn.ShouldBe(new DateOnly(2026, 9, 15)),
            e => e.AccountLabel!.Id.ShouldBe(label.Id));

        // bad values are refused with the field named
        var bad = await Client.PostJsonAsync($"/api/projects/{project.Id}/site-expenses",
            new { items = new[] { new { name = "X", amount = 1m, currency = "EUR", category = "luxury" } } }, superAdmin.AccessToken);
        ((int)bad.StatusCode).ShouldBe(400);
    }

    [Fact]
    public async Task Expenses_are_filtered_by_category_label_and_the_day_spent()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // no work price agreed yet, so the profit floor (tested on its own) stays out of the way
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 0m);
        var concrete = await AddLabelAsync(superAdmin.AccessToken);
        var finishing = await AddLabelAsync(superAdmin.AccessToken);
        await AddAsync(project.Id, superAdmin.AccessToken,
            new { name = "Ready-mix", amount = 500m, category = "materials", accountLabelId = concrete.Id, spentOn = "2026-05-02" },
            new { name = "Formwork crew", amount = 700m, category = "contractor", accountLabelId = concrete.Id, spentOn = "2026-05-20" },
            new { name = "Tiles", amount = 300m, category = "materials", accountLabelId = finishing.Id, spentOn = "2026-06-10" },
            new { name = "Permit fee", amount = 120m, category = "fees", spentOn = "2026-06-11" });

        (await ListAsync(project.Id, superAdmin.AccessToken, "?category=materials")).TotalCount.ShouldBe(2);
        (await ListAsync(project.Id, superAdmin.AccessToken, "?category=fees")).Items.Single().Name.ShouldBe("Permit fee");
        (await ListAsync(project.Id, superAdmin.AccessToken, "?category=equipment")).TotalCount.ShouldBe(0);
        (await ListAsync(project.Id, superAdmin.AccessToken, $"?accountLabelId={concrete.Id}")).Matching.Cost.ShouldBe(1200m);
        (await ListAsync(project.Id, superAdmin.AccessToken, $"?accountLabelId={finishing.Id}&category=contractor")).TotalCount.ShouldBe(0);
        // from and to are the day the money was spent
        (await ListAsync(project.Id, superAdmin.AccessToken, "?from=2026-05-15&to=2026-06-10")).Items.Select(e => e.Name).Order().ShouldBe(["Formwork crew", "Tiles"]);
        (await ListAsync(project.Id, superAdmin.AccessToken, "?from=2026-06-11")).Items.Single().Name.ShouldBe("Permit fee");
        (await ListAsync(project.Id, superAdmin.AccessToken)).All.Count.ShouldBe(4);
    }

    [Fact]
    public async Task An_archived_label_cannot_be_newly_picked_but_an_expense_keeps_the_one_it_has()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var keeping = await AddLabelAsync(superAdmin.AccessToken);
        var archived = await AddLabelAsync(superAdmin.AccessToken);
        var expense = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Gravel", amount = 80m, accountLabelId = keeping.Id }))[0];
        (await Client.PostEmptyAsync($"/api/account-labels/{keeping.Id}/archive", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/account-labels/{archived.Id}/archive", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // not newly picked: when adding, or when changing an expense that has another
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/site-expenses",
                new { items = new[] { new { name = "Sand", amount = 5m, accountLabelId = archived.Id } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.AccountLabelArchived);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{expense.Id}",
                new { name = "Gravel", amount = 80m, accountLabelId = archived.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.AccountLabelArchived);

        // but the expense keeps its own, shown by name, through a change that leaves it as it is
        var kept = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{expense.Id}",
            new { name = "Gravel", amount = 90m, accountLabelId = keeping.Id }, superAdmin.AccessToken)).ReadAsync<SiteExpenseResponse>();
        kept.Cost.ShouldBe(90m);
        kept.AccountLabel!.Name.ShouldBe(keeping.Name);

        // a label that doesn't exist is a validation error naming the field
        var unknown = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/site-expenses",
                new { items = new object[] { new { name = "Sand", amount = 5m }, new { name = "Sand", amount = 5m, accountLabelId = 999_999_999L } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        unknown.Errors!.Keys.ShouldContain("items[1].accountLabelId");
        (await ListAsync(project.Id, superAdmin.AccessToken)).All.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_old_materials_paths_still_answer_a_phone_batch_and_ignore_a_client_price()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.MaterialWorkerAsync(superAdmin);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var cement = Guid.NewGuid();

        // the phone's batch: its own reference on every item, and maybe the client price an older build still sends
        var response = await Client.PostJsonAsync($"/api/projects/{project.Id}/materials", new
        {
            items = new object[]
            {
                new { name = "Cement", quantity = 20m, unit = "bag", unitCost = 7.5m, clientRef = cement, clientPrice = 12m },
                new { name = "Sand", amount = 40m, clientRef = Guid.NewGuid() },
            },
        }, worker.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var added = await response.ReadAsync<List<SiteExpenseResponse>>();
        added.Select(e => e.Cost).ShouldBe([150m, 40m]);
        added.ShouldAllBe(e => e.Category == SiteExpenseCategory.Materials);

        // the same batch again adds nothing
        var again = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/materials",
            new { items = new[] { new { name = "Cement", quantity = 20m, unit = "bag", unitCost = 7.5m, clientRef = cement } } }, worker.AccessToken))
            .ReadAsync<List<SiteExpenseResponse>>();
        again.Single().Id.ShouldBe(added[0].Id);

        // reading, changing and removing answer on both paths, with the same rows
        var viaOld = await (await Client.GetAsync($"/api/projects/{project.Id}/materials?page=1&pageSize=30", worker.AccessToken)).ReadAsync<SiteExpenseListResponse>();
        var viaNew = await ListAsync(project.Id, worker.AccessToken);
        viaOld.Items.Select(e => e.Id).ShouldBe(viaNew.Items.Select(e => e.Id));
        viaOld.TotalCount.ShouldBe(2);

        var changed = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{added[1].Id}",
            new { name = "Sand", amount = 45m }, worker.AccessToken)).ReadAsync<SiteExpenseResponse>();
        changed.Cost.ShouldBe(45m);
        (await Client.DeleteAsync($"/api/projects/{project.Id}/materials/{added[1].Id}", worker.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await ListAsync(project.Id, worker.AccessToken)).TotalCount.ShouldBe(1);

        // a material's client price is gone on both paths
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/materials/{added[0].Id}/client-price", new { clientPrice = 1m }, superAdmin.AccessToken))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{added[0].Id}/client-price", new { clientPrice = 1m }, superAdmin.AccessToken))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_finalised_site_takes_no_expense_changes_until_reopened_admins_included()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var expense = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Sand", amount = 80m }))[0];

        // an expense is a cost: completing doesn't wait for anything to be sent to the client
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/site-expenses", new { items = new[] { new { name = "Late", amount = 5m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{expense.Id}", new { name = "Sand", amount = 90m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/site-expenses/{expense.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);

        var locked = await ListAsync(project.Id, superAdmin.AccessToken);
        locked.CanAdd.ShouldBeFalse();
        locked.Items.Single().CanEdit.ShouldBeFalse();

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/reopen", superAdmin.AccessToken);
        (await ListAsync(project.Id, superAdmin.AccessToken)).CanAdd.ShouldBeTrue();
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/site-expenses", new { items = new[] { new { name = "Late", amount = 5m } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task An_expense_can_change_and_be_removed_while_the_site_is_open_whatever_the_client_was_sent()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 0m);
        var expense = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Paint", quantity = 10m, unit = "litre", unitCost = 5m }))[0];

        // a client BOQ agreed and sent doesn't freeze the site's costs: they are plain costs
        var boq = await new BoqTestSteps(_steps).AgreeClientBoqAsync(superAdmin.AccessToken, project.Id, 500m);
        (await Client.PostJsonAsync($"/api/boqs/{boq.Id}/deliveries", new { channel = "download" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var changed = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{expense.Id}",
            new { name = "Paint", quantity = 12m, unit = "litre", unitCost = 5m, supplier = "Levant Paints" }, superAdmin.AccessToken)).ReadAsync<SiteExpenseResponse>();
        changed.ShouldSatisfyAllConditions(
            e => e.Cost.ShouldBe(60m),
            e => e.Supplier.ShouldBe("Levant Paints"));
        (await Client.DeleteAsync($"/api/projects/{project.Id}/site-expenses/{expense.Id}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await ListAsync(project.Id, superAdmin.AccessToken)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Expenses_are_filtered_grouped_and_paged()
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
    public async Task Other_domains_never_see_site_expenses_and_design_projects_have_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var expense = (await AddAsync(site.Id, superAdmin.AccessToken, new { name = "Gravel", amount = 50m }))[0];

        await (await Client.GetAsync($"/api/projects/{site.Id}/site-expenses", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        // These permissions belong to Execution, so a Design admin is refused before any lookup.
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/site-expenses/{expense.Id}", new { name = "X", amount = 1m }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{design.Id}/site-expenses", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.PostJsonAsync($"/api/projects/{design.Id}/site-expenses", new { items = new[] { new { name = "X", amount = 1m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    private async Task<AccountLabelResponse> AddLabelAsync(string accessToken)
    {
        var response = await Client.PostJsonAsync("/api/account-labels", new { name = "Label " + Guid.NewGuid().ToString("N")[..8] }, accessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AccountLabelResponse>();
    }

    [Fact]
    public async Task An_expense_in_another_currency_needs_a_rate_on_its_day_so_profit_never_misses_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await LbpRateAsync(superAdmin.AccessToken);

        // before the first LBP rate nothing converts it, so the site's money would leave it out: it is refused instead
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/site-expenses",
                new { items = new[] { new { name = "Old stock", amount = 179_000m, currency = "LBP", spentOn = "2025-06-01" } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExchangeRateMissing);
        var sand = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Sand", amount = 50m }))[0];
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/site-expenses/{sand.Id}",
                new { name = "Sand", amount = 4_475_000m, currency = "LBP", spentOn = "2025-06-01" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExchangeRateMissing);

        // on a day with a rate it is recorded, and the feed says which currency its amount is in
        var crane = (await AddAsync(project.Id, superAdmin.AccessToken, new { name = "Crane hire", amount = 895_000m, currency = "LBP", spentOn = "2026-09-14" }))[0];
        var feed = await (await Client.GetAsync($"/api/activity?projectId={project.Id}", superAdmin.AccessToken)).ReadAsync<PagedResponse<ActivityItemResponse>>();
        feed.Items.Single(i => i.Kind == ActivityEventKinds.MaterialAdded && i.SubjectId == crane.Id)
            .Payload!.Value.GetProperty("currency").GetString().ShouldBe("LBP");
    }

    /// <summary>89,500 LBP to the dollar from 1 January 2026. Rates are company-wide and added in date order: a repeat is refused, which is fine.</summary>
    private Task LbpRateAsync(string token)
        => Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_500m, effectiveFrom = "2026-01-01" }, token);

    private async Task<IReadOnlyList<SiteExpenseResponse>> AddAsync(long projectId, string accessToken, params object[] items)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/site-expenses", new { items }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<SiteExpenseResponse>>();
    }

    private async Task<SiteExpenseListResponse> ListAsync(long projectId, string accessToken, string query = "")
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/site-expenses{query}", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<SiteExpenseListResponse>();
    }
}
