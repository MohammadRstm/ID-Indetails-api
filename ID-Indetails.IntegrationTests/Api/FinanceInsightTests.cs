using System.Text.Json;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;
using static ID_Indetails.IntegrationTests.Api.BoqTestSteps;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Financial Intelligence on one project: its profitability, where its profit comes from, and simulated changes; and
/// across a domain: its projects' risks and its spending. Every test follows its own projects, since the classes share
/// one database.
/// </summary>
public class FinanceInsightTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private BoqTestSteps Boqs => new(_steps);

    private static string InsightUrl(long projectId) => $"/api/finance/insights/projects/{projectId}";

    private static string ScenarioUrl(long projectId, string query) => $"/api/finance/insights/projects/{projectId}/scenario?{query}";

    private static string RisksUrl(string domain) => $"/api/finance/insights/{domain}/risks";

    private static string ExpensesUrl(string domain, string? query = null)
        => $"/api/finance/insights/{domain}/expenses" + (query is null ? "" : $"?{query}");

    private static string LabourUrl(string domain, string? query = null)
        => $"/api/finance/insights/{domain}/labour" + (query is null ? "" : $"?{query}");

    /// <summary>Every property name anywhere in a JSON value, nested objects and arrays included.</summary>
    private static IEnumerable<string> PropertyNames(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
            _ => [],
        };

    /// <summary>The first Tuesday of a month, a working day, so its hours fall in working hours.</summary>
    private static DateOnly FirstTuesday(DateOnly month)
        => Enumerable.Range(0, 7).Select(month.AddDays).First(d => d.DayOfWeek == DayOfWeek.Tuesday);

    private static DateOnly CompanyToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));

    private async Task<T> OkAsync<T>(string url, AuthResponse who)
    {
        var response = await Client.GetAsync(url, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    /// <summary>A 200's body read both ways: as the response, and as the JSON it was sent as.</summary>
    private async Task<(T Response, JsonElement Json)> OkJsonAsync<T>(string url, AuthResponse who)
    {
        var response = await Client.GetAsync(url, who.AccessToken);
        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(200, body);
        using var json = JsonDocument.Parse(body);
        return (JsonSerializer.Deserialize<T>(body, ApiClientExtensions.Json)!, json.RootElement.Clone());
    }

    private async Task<ExpenseResponse> ExpenseAsync(long projectId, string label, decimal amount, DateOnly spentOn, string token)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/expenses",
            new { label, amount, currency = "USD", spentOn = spentOn.ToString("yyyy-MM-dd") }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ExpenseResponse>();
    }

    private async Task<ProjectMoneyResponse> MoneyAsync(AuthResponse who, long projectId)
        => (await OkAsync<ProjectResponse>($"/api/projects/{projectId}", who)).Money!;

    private static IEnumerable<(BridgeStepKind Kind, decimal Amount)> Steps(ProjectInsightResponse insight)
        => insight.Bridge.Select(s => (s.Kind, s.Amount));

    private async Task PayAsync(long projectId, decimal amount, string token)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/payments", new { amount, currency = "USD" }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A site at 10% supervision and 11% TVA whose client BOQ is agreed: a work line of <paramref name="work"/>, and a permit
    /// line of <paramref name="permits"/> when above 0.
    /// </summary>
    private async Task<(ProjectResponse Site, BoqDetailResponse Boq)> SiteAsync(AuthResponse superAdmin, decimal work, decimal permits = 0)
    {
        var token = superAdmin.AccessToken;
        var site = await Projects.CreateProjectAsync(token, DomainCodes.Execution, projectFee: 0m);
        var priced = await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing",
            new { supervisionRate = 0.10m, tvaRate = 0.11m, description = "The usual rates" }, token);
        priced.IsSuccessStatusCode.ShouldBeTrue(await priced.Content.ReadAsStringAsync());

        var lines = new List<object> { Line("Whole building", 1, work, unit: "lot") };
        if (permits > 0)
        {
            lines.Add(Line("Building permit", 1, permits, unit: "L.S", nature: "permit"));
        }

        var boq = await Boqs.CreateAsync(token, site.Id);
        await Boqs.SaveAsync(token, boq.Id, lines);
        return (site, await Boqs.AgreeAsync(token, boq.Id));
    }

    [Fact]
    public async Task Only_callers_who_see_the_money_of_the_projects_domain_get_its_insights()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, plainAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designMoneyAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        const string lever = "extraCost=100&extraCostSource=expenses";

        (await OkAsync<ProjectInsightResponse>(InsightUrl(design.Id), superAdmin)).ProjectId.ShouldBe(design.Id);
        (await OkAsync<ProjectInsightResponse>(InsightUrl(site.Id), superAdmin)).Domain.ShouldBe(DomainCodes.Execution);
        (await OkAsync<ProjectScenarioResponse>(ScenarioUrl(design.Id, lever), superAdmin)).ProjectId.ShouldBe(design.Id);
        (await OkAsync<ProjectInsightResponse>(InsightUrl(design.Id), designMoneyAdmin)).ProjectId.ShouldBe(design.Id);
        (await OkAsync<ProjectScenarioResponse>(ScenarioUrl(design.Id, lever), designMoneyAdmin)).ProjectId.ShouldBe(design.Id);

        // without financial:view: the policy refuses
        await (await Client.GetAsync(InsightUrl(design.Id), plainAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(ScenarioUrl(design.Id, lever), plainAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // a domain the caller can't see reads as not found, as does a project that doesn't exist
        await (await Client.GetAsync(InsightUrl(site.Id), designMoneyAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(ScenarioUrl(site.Id, "extraCost=100&extraCostSource=site_expense"), designMoneyAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(InsightUrl(999_999_999), superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_design_project_shows_its_money_where_its_profit_comes_from_and_its_departments_without_anyone_in_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var architecture = await _steps.DesignDepartmentAsync();
        var (designerEmail, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // 1,600 a month is 10 an hour in February 2026 (160 paid hours)
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = "2026-01-01" }, token)).IsSuccessStatusCode.ShouldBeTrue();
        var client = await Projects.CreateClientAsync(token, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 10_000m,
            tvaRate = 0.11m,
            departmentIds = new[] { architecture },
        }, token);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var project = await created.ReadAsync<ProjectResponse>();

        // a VO of 2,000 (all Architecture's), a 300 expense, and 3 hours in Architecture on Tuesday 10 February 2026
        // (09:00-12:00 in Beirut) with the paid hour of break: 40 of labour
        var vo = await Client.PostJsonAsync($"/api/projects/{project.Id}/design-vos", new
        {
            title = "Guest house",
            lines = new[] { new { description = "Guest house design", amount = 2_000m } },
            split = "shares",
            apply = true,
        }, token);
        ((int)vo.StatusCode).ShouldBe(201, await vo.Content.ReadAsStringAsync());
        var expense = await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Prints", amount = 300m, currency = "USD" }, token);
        ((int)expense.StatusCode).ShouldBe(201, await expense.Content.ReadAsStringAsync());
        await _steps.ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, team_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, @t, 'Drawings', '2026-02-10 07:00:00', '2026-02-10 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id), ("@t", architecture));

        var money = await MoneyAsync(superAdmin, project.Id);
        var response = await Client.GetAsync(InsightUrl(project.Id), token);
        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(200, body);
        var insight = JsonSerializer.Deserialize<ProjectInsightResponse>(body, ApiClientExtensions.Json)!;

        // the same figures as the project's money: 12,000 agreed, 13,320 with TVA, 340 spent
        money.Cost.Labour.ShouldBe(40m);
        insight.ShouldSatisfyAllConditions(
            i => i.Domain.ShouldBe(DomainCodes.Design),
            i => i.BillingType.ShouldBe(BillingType.Client),
            i => i.AgreedRevenue.ShouldBe(12_000m),
            i => i.AgreedRevenue.ShouldBe(money.Revenue),
            i => i.AgreedWithTva.ShouldBe(13_320m),
            i => i.AgreedWithTva.ShouldBe(money.RevenueWithTva),
            i => i.Cost.Labour.ShouldBe(money.Cost.Labour),
            i => i.Cost.Expenses.ShouldBe(300m),
            i => i.Cost.Total.ShouldBe(money.Cost.Total),
            i => i.AgreedProfit.ShouldBe(11_660m),
            i => i.AgreedProfit.ShouldBe(money.Profit),
            i => i.Margin.ShouldBe(money.Margin),
            i => i.Standing.ShouldBe(ProfitStanding.Fine),
            i => i.Owed.ShouldBe(13_320m),
            i => i.Owed.ShouldBe(money.Billing.Owed),
            i => i.SiteCashProfit.ShouldBeNull(),
            i => i.SupervisionRate.ShouldBeNull(),
            i => i.TvaRate.ShouldBe(0.11m));

        // from the payment to the profit, step by step
        Steps(insight).ShouldBe([
            (BridgeStepKind.BasePrice, 10_000m),
            (BridgeStepKind.VariationOrders, 2_000m),
            (BridgeStepKind.Labour, -40m),
            (BridgeStepKind.Expenses, -300m)]);
        insight.Bridge.Sum(s => s.Amount).ShouldBe(insight.AgreedProfit);

        // Architecture's budget (the price before VOs × its whole share, plus the VO), its labour and what is left
        var architectureMoney = (await OkAsync<ProjectDepartmentsResponse>($"/api/projects/{project.Id}/departments", superAdmin))
            .Departments.Single(d => d.Id == architecture).Money!;
        var row = insight.Departments.ShouldNotBeNull().ShouldHaveSingleItem();
        row.ShouldSatisfyAllConditions(
            r => r.Id.ShouldBe(architecture),
            r => r.Share.ShouldBe(1m),
            r => r.Budget.ShouldBe(12_000m),
            r => r.Budget.ShouldBe(architectureMoney.Budget),
            r => r.Cost.ShouldBe(40m),
            r => r.Cost.ShouldBe(architectureMoney.Cost),
            r => r.Left.ShouldBe(11_960m),
            r => r.Minutes.ShouldBe(180),
            r => r.LabourShare.ShouldBe(1m),
            r => r.Unconverted.ShouldBeEmpty(),
            // no hours planned for it yet, so no progress by hours
            r => r.PlannedMinutes.ShouldBeNull(),
            r => r.Progress.ShouldBeNull());

        // never a person: no people, no one's hours, rate or name
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("departments")[0].EnumerateObject().Select(p => p.Name).ShouldBe(
            ["id", "name", "share", "budget", "cost", "left", "minutes", "labourShare", "unconverted", "plannedMinutes", "progress"], ignoreOrder: true);
        body.ShouldNotContain("\"people\"");
        body.ShouldNotContain("\"person\"");
        body.ShouldNotContain("hourlyRate");
        body.ShouldNotContain(designerEmail, Case.Insensitive);
        body.ShouldNotContain("Test employee", Case.Insensitive);
    }

    [Fact]
    public async Task A_sites_bridge_starts_at_its_client_boq_adds_its_vos_supervision_and_permits_and_shows_a_settlement()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var (site, boq) = await SiteAsync(superAdmin, work: 10_000m, permits: 2_000m);
        var vo = await Boqs.AgreeVoAsync(token, site.Id, 1_000m);
        await PayAsync(site.Id, 5_000m, token);
        var spent = await Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses", new { items = new[] { new { name = "Cement", amount = 400m } } }, token);
        spent.IsSuccessStatusCode.ShouldBeTrue(await spent.Content.ReadAsStringAsync());

        // 11,000 of work + 1,100 supervision + 2,000 permits = 14,100 agreed; 15,310 with the TVA on the work; 5,000 paid
        var money = await MoneyAsync(superAdmin, site.Id);
        var insight = await OkAsync<ProjectInsightResponse>(InsightUrl(site.Id), superAdmin);

        money.CashProfit.ShouldNotBeNull();
        insight.ShouldSatisfyAllConditions(
            i => i.AgreedRevenue.ShouldBe(14_100m),
            i => i.AgreedRevenue.ShouldBe(money.Revenue),
            i => i.AgreedWithTva.ShouldBe(15_310m),
            i => i.AgreedWithTva.ShouldBe(money.RevenueWithTva),
            i => i.Cost.SiteExpenses.ShouldBe(400m),
            i => i.AgreedProfit.ShouldBe(13_700m),
            i => i.AgreedProfit.ShouldBe(money.Profit),
            i => i.SiteCashProfit.ShouldBe(money.CashProfit),
            i => i.ToPocket.ShouldBe(0m),
            i => i.FromPocket.ShouldBe(0m),
            i => i.Received.ShouldBe(5_000m),
            i => i.Owed.ShouldBe(10_310m),
            i => i.Owed.ShouldBe(money.Billing.Owed),
            i => i.SupervisionRate.ShouldBe(0.10m),
            i => i.Standing.ShouldBe(ProfitStanding.Fine),
            i => i.Departments.ShouldBeNull());
        boq.TotalUsd.ShouldBe(10_000m);
        vo.TotalUsd.ShouldBe(1_000m);
        Steps(insight).ShouldBe([
            (BridgeStepKind.BasePrice, boq.TotalUsd),
            (BridgeStepKind.VariationOrders, vo.TotalUsd),
            (BridgeStepKind.Supervision, 1_100m),
            (BridgeStepKind.Permits, 2_000m),
            (BridgeStepKind.SiteExpenses, -400m)]);
        insight.Bridge.Sum(s => s.Amount).ShouldBe(insight.AgreedProfit);

        // completed with the rest excused: a VO takes the work down to 2,479.34, whose 247.93 supervision, 272.73 TVA
        // and the 2,000 permits come to the 5,000 paid
        var completed = await Client.PostJsonAsync($"/api/projects/{site.Id}/complete", new { settleOwed = "excused" }, token);
        ((int)completed.StatusCode).ShouldBe(200, await completed.Content.ReadAsStringAsync());
        var excused = (await OkAsync<BoqListResponse>($"/api/projects/{site.Id}/boqs", superAdmin)).Vos.Single(v => v.Title == BoqRules.ExcusedTitle);
        excused.TotalUsd.ShouldBe(-8_520.66m);

        var closed = await MoneyAsync(superAdmin, site.Id);
        var settled = await OkAsync<ProjectInsightResponse>(InsightUrl(site.Id), superAdmin);

        settled.ShouldSatisfyAllConditions(
            i => i.Status.ShouldBe(ProjectStatus.Completed),
            i => i.AgreedRevenue.ShouldBe(4_727.27m),
            i => i.AgreedRevenue.ShouldBe(closed.Revenue),
            i => i.AgreedProfit.ShouldBe(closed.Profit),
            i => i.Owed.ShouldBe(0m));
        Steps(settled).ShouldBe([
            (BridgeStepKind.BasePrice, 10_000m),
            (BridgeStepKind.VariationOrders, 1_000m),
            // the settlement carries the 852.07 supervision it took off; the supervision step stays that of the base and VO
            (BridgeStepKind.Settlement, excused.TotalUsd - 852.07m),
            (BridgeStepKind.Supervision, 1_100m),
            (BridgeStepKind.Permits, 2_000m),
            (BridgeStepKind.SiteExpenses, -400m)]);
        settled.Bridge.Sum(s => s.Amount).ShouldBe(settled.AgreedProfit);
    }

    [Fact]
    public async Task A_vo_scenario_starts_from_todays_figures_and_prices_the_vo_with_its_supervision_and_tva()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (site, _) = await SiteAsync(superAdmin, work: 10_000m, permits: 2_000m);
        await PayAsync(site.Id, 3_000m, superAdmin.AccessToken);
        var today = await OkAsync<ProjectInsightResponse>(InsightUrl(site.Id), superAdmin);

        var scenario = await OkAsync<ProjectScenarioResponse>(ScenarioUrl(site.Id, "voAmount=1000"), superAdmin);

        scenario.ProjectId.ShouldBe(site.Id);
        scenario.Before.ShouldSatisfyAllConditions(
            b => b.AgreedRevenue.ShouldBe(today.AgreedRevenue),
            b => b.AgreedWithTva.ShouldBe(today.AgreedWithTva),
            b => b.Cost.ShouldBe(today.Cost.Total),
            b => b.AgreedProfit.ShouldBe(today.AgreedProfit),
            b => b.Owed.ShouldBe(today.Owed),
            b => b.Standing.ShouldBe(today.Standing));
        // 10,000 of work + 1,000 supervision + 2,000 permits = 13,000; 14,100 with the TVA, of which 3,000 is paid
        scenario.Before.ShouldSatisfyAllConditions(
            b => b.AgreedRevenue.ShouldBe(13_000m),
            b => b.AgreedWithTva.ShouldBe(14_100m),
            b => b.Owed.ShouldBe(11_100m));
        // the VO adds 1,000 of work and 100 of supervision to revenue, and 110 of TVA on the work to what the client pays
        scenario.After.ShouldSatisfyAllConditions(
            a => a.AgreedRevenue.ShouldBe(14_100m),
            a => a.AgreedWithTva.ShouldBe(15_310m),
            a => a.Cost.ShouldBe(0m),
            a => a.AgreedProfit.ShouldBe(14_100m),
            a => a.Owed.ShouldBe(12_310m),
            a => a.Standing.ShouldBe(ProfitStanding.Fine));
        scenario.ProfitChange.ShouldBe(1_100m);
        scenario.FloorWouldRefuse.ShouldBeFalse();
        scenario.WeeklyCost.ShouldBeNull();
        scenario.Basis.Assumptions.ShouldContain(a => a.StartsWith("A variation order of 1,000 USD"));

        // nothing is saved
        (await MoneyAsync(superAdmin, site.Id)).Revenue.ShouldBe(13_000m);
    }

    [Fact]
    public async Task An_extra_cost_that_takes_profit_to_zero_or_below_would_be_refused_by_the_floor()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // 5,000 agreed, untaxed, nothing spent yet
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 5_000m);

        var refused = await OkAsync<ProjectScenarioResponse>(ScenarioUrl(design.Id, "extraCost=6000&extraCostSource=expenses"), superAdmin);

        refused.Before.ShouldSatisfyAllConditions(
            b => b.AgreedProfit.ShouldBe(5_000m),
            b => b.Standing.ShouldBe(ProfitStanding.Fine));
        refused.After.ShouldSatisfyAllConditions(
            a => a.AgreedRevenue.ShouldBe(5_000m),
            a => a.Cost.ShouldBe(6_000m),
            a => a.AgreedProfit.ShouldBe(-1_000m),
            a => a.Margin.ShouldBe(-0.2m),
            a => a.Owed.ShouldBe(refused.Before.Owed),
            a => a.Standing.ShouldBe(ProfitStanding.AtZero));
        refused.ProfitChange.ShouldBe(-6_000m);
        refused.FloorWouldRefuse.ShouldBeTrue();

        // a smaller one leaves 400, 8% of the price: close to zero, and it would pass
        var close = await OkAsync<ProjectScenarioResponse>(ScenarioUrl(design.Id, "extraCost=4600&extraCostSource=labour"), superAdmin);
        close.After.Standing.ShouldBe(ProfitStanding.Close);
        close.FloorWouldRefuse.ShouldBeFalse();

        // nothing is saved
        (await MoneyAsync(superAdmin, design.Id)).Cost.Total.ShouldBe(0m);
    }

    [Fact]
    public async Task Levers_that_do_not_fit_the_project_are_refused_as_the_real_change_would_be()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var site = await Projects.CreateProjectAsync(token, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 5_000m);
        var internalWork = await (await Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling", billingType = "internal", domainCode = DomainCodes.Design }, token)).ReadAsync<ProjectResponse>();

        // a site's costs are site expenses or ground labour, Design's labour or expenses; only a site has supervision
        await (await Client.GetAsync(ScenarioUrl(site.Id, "extraCost=100&extraCostSource=labour"), token))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.GetAsync(ScenarioUrl(design.Id, "extraCost=100&extraCostSource=site_expense"), token))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.GetAsync(ScenarioUrl(design.Id, "supervisionRate=0.2"), token))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        (await OkAsync<ProjectScenarioResponse>(ScenarioUrl(site.Id, "extraCost=100&extraCostSource=ground_labour"), superAdmin))
            .ProfitChange.ShouldBe(-100m);

        // nothing to settle once the client paid it all
        await PayAsync(design.Id, 5_000m, token);
        await (await Client.GetAsync(ScenarioUrl(design.Id, "settleOwed=excused"), token))
            .ShouldFailWithAsync(422, ErrorCodes.NothingOwed);

        // internal work has no price: only a cost or a delay, and no floor to hold it to
        await (await Client.GetAsync(ScenarioUrl(internalWork.Id, "voAmount=1000"), token))
            .ShouldFailWithAsync(422, ErrorCodes.ScenarioNotApplicable);
        await (await Client.GetAsync(ScenarioUrl(internalWork.Id, "tvaRate=0.2"), token))
            .ShouldFailWithAsync(422, ErrorCodes.ScenarioNotApplicable);
        var cost = await OkAsync<ProjectScenarioResponse>(ScenarioUrl(internalWork.Id, "extraCost=100&extraCostSource=expenses&delayWeeks=4"), superAdmin);
        cost.ShouldSatisfyAllConditions(
            s => s.Before.Standing.ShouldBeNull(),
            s => s.After.AgreedRevenue.ShouldBe(0m),
            s => s.After.Cost.ShouldBe(s.Before.Cost + 100m),
            s => s.ProfitChange.ShouldBe(-100m),
            // started this week, it has no full week of cost to price a delay at
            s => s.WeeklyCost.ShouldBe(0m),
            s => s.FloorWouldRefuse.ShouldBeFalse());

        // no change at all is a validation error
        await (await Client.GetAsync($"/api/finance/insights/projects/{design.Id}/scenario", token))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_domains_risks_and_spending_need_financial_view_and_a_business_domain_the_caller_sees()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, plainAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designMoneyAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);

        foreach (var domain in new[] { DomainCodes.Design, DomainCodes.Execution })
        {
            (await OkAsync<ProjectRisksResponse>(RisksUrl(domain), superAdmin)).Domain.ShouldBe(domain);
            (await OkAsync<ExpenseTrendsResponse>(ExpensesUrl(domain), superAdmin)).Domain.ShouldBe(domain);
        }

        (await OkAsync<ProjectRisksResponse>(RisksUrl(DomainCodes.Design), designMoneyAdmin)).Domain.ShouldBe(DomainCodes.Design);
        (await OkAsync<ExpenseTrendsResponse>(ExpensesUrl(DomainCodes.Design), designMoneyAdmin)).Domain.ShouldBe(DomainCodes.Design);

        // without financial:view: the policy refuses
        await (await Client.GetAsync(RisksUrl(DomainCodes.Design), plainAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync(ExpensesUrl(DomainCodes.Design), plainAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // the website is a service with no projects' money, and a domain that doesn't exist isn't found either
        foreach (var domain in new[] { DomainCodes.Website, "nowhere" })
        {
            await (await Client.GetAsync(RisksUrl(domain), superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
            await (await Client.GetAsync(ExpensesUrl(domain), superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        }

        // the other business domain reads as not found to an admin of one
        await (await Client.GetAsync(RisksUrl(DomainCodes.Execution), designMoneyAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(ExpensesUrl(DomainCodes.Execution), designMoneyAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_design_client_project_whose_cost_passed_its_agreed_amount_is_at_zero_and_internal_work_is_never_a_risk()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;

        // 1,000 agreed, untaxed; a 1,500 expense the floor kept aside, then forced through: a profit of -500
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 1_000m);
        var refused = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Structural survey", amount = 1_500m, currency = "USD" }, token)).ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        var forced = await Client.PostEmptyAsync($"/api/blocked-changes/{refused.BlockedChangeId.ShouldNotBeNull()}/force", token);
        forced.IsSuccessStatusCode.ShouldBeTrue(await forced.Content.ReadAsStringAsync());

        // internal work with a cost and no price: a cost centre
        var created = await Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling " + Guid.NewGuid().ToString("N")[..8], billingType = "internal", domainCode = DomainCodes.Design }, token);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var internalWork = await created.ReadAsync<ProjectResponse>();
        await ExpenseAsync(internalWork.Id, "Plotter", 300m, CompanyToday(), token);

        var (risks, json) = await OkJsonAsync<ProjectRisksResponse>(RisksUrl(DomainCodes.Design), superAdmin);

        risks.Domain.ShouldBe(DomainCodes.Design);
        var mine = risks.Projects.Where(p => p.ProjectId == project.Id).ShouldHaveSingleItem();
        mine.ShouldSatisfyAllConditions(
            p => p.Title.ShouldBe(project.Title),
            p => p.Status.ShouldBe(ProjectStatus.Active));
        // the most pressing first
        mine.Risks[0].ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ProfitAtZero),
            r => r.Amount.ShouldBe(-500m),
            r => r.Fact.ShouldBe("An agreed profit of -500 USD on 1,000 USD agreed."),
            r => r.Rule.ShouldBe(ProjectRiskRules.ProfitAtZeroRule));
        risks.Counts.ShouldContain(c => c.Kind == ProjectRiskKind.ProfitAtZero);
        risks.Projects.ShouldNotContain(p => p.ProjectId == internalWork.Id);

        // kinds in snake_case, names in camelCase
        json.EnumerateObject().Select(p => p.Name).ShouldBe(["domain", "today", "checked", "projects", "counts", "basis"], ignoreOrder: true);
        var group = json.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("projectId").GetInt64() == project.Id);
        group.EnumerateObject().Select(p => p.Name).ShouldBe(["projectId", "title", "status", "risks"], ignoreOrder: true);
        group.GetProperty("status").GetString().ShouldBe("active");
        var atZero = group.GetProperty("risks")[0];
        atZero.EnumerateObject().Select(p => p.Name).ShouldBe(["kind", "amount", "fact", "rule"], ignoreOrder: true);
        atZero.GetProperty("kind").GetString().ShouldBe("profit_at_zero");
    }

    [Fact]
    public async Task The_same_expense_twice_on_one_day_is_unusual_and_the_months_end_with_this_one()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 5_000m);
        var today = CompanyToday();
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var label = "Plotter paper " + Guid.NewGuid().ToString("N")[..6];
        var first = await ExpenseAsync(project.Id, label, 120m, today, token);
        var second = await ExpenseAsync(project.Id, label, 120m, today, token);

        var (trends, json) = await OkJsonAsync<ExpenseTrendsResponse>(ExpensesUrl(DomainCodes.Design), superAdmin);

        var twice = trends.Anomalies.Where(a => a.ProjectId == project.Id).ShouldHaveSingleItem();
        twice.ShouldSatisfyAllConditions(
            a => a.Kind.ShouldBe(ExpenseAnomalyKind.SameAmountTwice),
            a => a.On.ShouldBe(today),
            a => a.ProjectTitle.ShouldBe(project.Title),
            a => a.Item.ShouldBe(label),
            a => a.Category.ShouldBe("other"),
            a => a.Usd.ShouldBe(120m),
            a => a.ExpenseIds.ShouldBe([first.Id, second.Id], ignoreOrder: true));

        // 12 months when not asked, oldest first, ending with this one; every category in every month
        trends.Months.Select(m => m.Month).ShouldBe(Enumerable.Range(0, 12).Select(i => thisMonth.AddMonths(i - 11)));
        trends.Categories.ShouldContain("other");
        trends.Months.ShouldAllBe(m => m.Categories.Select(c => c.Category).SequenceEqual(trends.Categories));

        // kinds in snake_case, names in camelCase
        json.EnumerateObject().Select(p => p.Name).ShouldBe(["domain", "categories", "months", "anomalies", "unconverted", "basis"], ignoreOrder: true);
        var anomaly = json.GetProperty("anomalies").EnumerateArray()
            .Single(a => a.TryGetProperty("projectId", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == project.Id);
        anomaly.GetProperty("kind").GetString().ShouldBe("same_amount_twice");
        anomaly.GetProperty("expenseIds").EnumerateArray().Select(e => e.GetInt64()).ShouldBe([first.Id, second.Id], ignoreOrder: true);
        json.GetProperty("months")[11].GetProperty("month").GetString().ShouldBe(thisMonth.ToString("yyyy-MM-dd"));

        // fewer months on asking; 1 to 24
        (await OkAsync<ExpenseTrendsResponse>(ExpensesUrl(DomainCodes.Design, "months=3"), superAdmin)).Months.Select(m => m.Month)
            .ShouldBe([thisMonth.AddMonths(-2), thisMonth.AddMonths(-1), thisMonth]);
        await (await Client.GetAsync(ExpensesUrl(DomainCodes.Design, "months=25"), token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync(ExpensesUrl(DomainCodes.Design, "months=0"), token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Design_labour_needs_financial_view_and_is_refused_for_execution_and_unknown_to_other_domains()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, plainAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designMoneyAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, executionMoneyAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);

        (await OkAsync<LabourInsightResponse>(LabourUrl(DomainCodes.Design), superAdmin)).Domain.ShouldBe(DomainCodes.Design);
        (await OkAsync<LabourInsightResponse>(LabourUrl(DomainCodes.Design), designMoneyAdmin)).Domain.ShouldBe(DomainCodes.Design);

        // without financial:view: the policy refuses
        await (await Client.GetAsync(LabourUrl(DomainCodes.Design), plainAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // Execution logs no hours: its labour is ground labour, in its spending
        await (await Client.GetAsync(LabourUrl(DomainCodes.Execution), superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
        await (await Client.GetAsync(LabourUrl(DomainCodes.Execution), executionMoneyAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);

        // the website and a domain that doesn't exist aren't found, nor the business domain an admin doesn't see
        foreach (var domain in new[] { DomainCodes.Website, "nowhere" })
        {
            await (await Client.GetAsync(LabourUrl(domain), superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        }

        await (await Client.GetAsync(LabourUrl(DomainCodes.Design), executionMoneyAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync(LabourUrl(DomainCodes.Execution), designMoneyAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Design_labour_shows_a_projects_hours_and_its_outside_scope_part_over_12_months_without_anyone_in_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var architecture = await _steps.DesignDepartmentAsync();
        var today = CompanyToday();
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var lastMonth = thisMonth.AddMonths(-1);
        var (designerEmail, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
                new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = lastMonth.AddMonths(-1).ToString("yyyy-MM-dd") }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        var client = await Projects.CreateClientAsync(token, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 10_000m,
            tvaRate = 0m,
            departmentIds = new[] { architecture },
        }, token);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var project = await created.ReadAsync<ProjectResponse>();

        // on last month's first Tuesday, in the morning in Beirut: 2 hours within the scope, then 30 minutes outside it
        var day = FirstTuesday(lastMonth).ToString("yyyy-MM-dd");
        const string insert =
            "INSERT INTO work_entries (user_id, project_id, team_id, description, started_at, ended_at, outside_scope, created_at) " +
            "VALUES (@u, @p, @t, @d, @s, @e, @o, UTC_TIMESTAMP(3))";
        await _steps.ExecuteAsync(insert, ("@u", designer.User.Id), ("@p", project.Id), ("@t", architecture), ("@d", "Drawings"),
            ("@s", $"{day} 07:00:00"), ("@e", $"{day} 09:00:00"), ("@o", false));
        await _steps.ExecuteAsync(insert, ("@u", designer.User.Id), ("@p", project.Id), ("@t", architecture), ("@d", "A guest house the client added"),
            ("@s", $"{day} 09:00:00"), ("@e", $"{day} 09:30:00"), ("@o", true));

        var (labour, json) = await OkJsonAsync<LabourInsightResponse>(LabourUrl(DomainCodes.Design), superAdmin);

        // 12 months when not asked, oldest first, ending with this one
        labour.Domain.ShouldBe(DomainCodes.Design);
        labour.Months.Select(m => m.Month).ShouldBe(Enumerable.Range(0, 12).Select(i => thisMonth.AddMonths(i - 11)));
        json.GetProperty("months")[11].GetProperty("month").GetString().ShouldBe(thisMonth.ToString("yyyy-MM-dd"));

        var mine = labour.Projects.Where(p => p.ProjectId == project.Id).ShouldHaveSingleItem();
        mine.ShouldSatisfyAllConditions(
            p => p.Title.ShouldBe(project.Title),
            p => p.BillingType.ShouldBe(BillingType.Client),
            p => p.Totals.Minutes.ShouldBe(150),
            p => p.Totals.OutsideScopeMinutes.ShouldBe(30),
            p => p.Totals.Cost.ShouldBeGreaterThan(0m),
            // the worked half hour, never the day's paid break
            p => p.Totals.OutsideScopeCost.ShouldBeGreaterThan(0m),
            p => p.Totals.OutsideScopeCost.ShouldBeLessThan(p.Totals.Cost),
            p => p.Totals.OutsideScopeShare.ShouldNotBeNull(),
            p => p.LabourShare.ShouldNotBeNull());

        // the same labour as the project's money and its department's, all time (all of it is in the window)
        var money = await MoneyAsync(superAdmin, project.Id);
        mine.Totals.Cost.ShouldBe(money.Cost.Labour);
        var department = (await OkAsync<ProjectDepartmentsResponse>($"/api/projects/{project.Id}/departments", superAdmin))
            .Departments.Single(d => d.Id == architecture).Money!;
        department.Minutes.ShouldBe(150);
        department.Cost.ShouldBe(mine.Totals.Cost);

        // being outside the scope changes nothing on the department: the same minutes, cost and what is left with it in the scope
        await _steps.ExecuteAsync("UPDATE work_entries SET outside_scope = 0 WHERE project_id = @p", ("@p", project.Id));
        var withinScope = (await OkAsync<ProjectDepartmentsResponse>($"/api/projects/{project.Id}/departments", superAdmin))
            .Departments.Single(d => d.Id == architecture).Money!;
        withinScope.Minutes.ShouldBe(department.Minutes);
        withinScope.Cost.ShouldBe(department.Cost);
        withinScope.Left.ShouldBe(department.Left);

        // last month holds the hours, and the department they were logged in has them
        labour.Months[10].Totals.Minutes.ShouldBeGreaterThanOrEqualTo(150);
        labour.Months[10].Totals.OutsideScopeMinutes.ShouldBeGreaterThanOrEqualTo(30);
        labour.Departments.ShouldContain(d => d.TeamId == architecture && d.Totals.Minutes >= 150);

        // never a person: no one's id, name, email or rate anywhere
        PropertyNames(json).ShouldNotContain(n =>
            n.Contains("user", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("person", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("people", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("employee", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("firstName", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("lastName", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("rate", StringComparison.OrdinalIgnoreCase));
        var body = json.GetRawText();
        body.ShouldNotContain(designerEmail, Case.Insensitive);
        body.ShouldNotContain("Test employee", Case.Insensitive);

        // fewer months on asking; 1 to 24
        (await OkAsync<LabourInsightResponse>(LabourUrl(DomainCodes.Design, "months=3"), superAdmin)).Months.Select(m => m.Month)
            .ShouldBe([thisMonth.AddMonths(-2), lastMonth, thisMonth]);
        await (await Client.GetAsync(LabourUrl(DomainCodes.Design, "months=25"), token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync(LabourUrl(DomainCodes.Design, "months=0"), token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
