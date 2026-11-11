using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton's Financial Intelligence tools, with a scripted model: one project's profitability and where it comes from,
/// and what a change would do to it. They need financial:view (the endpoint's own policy), another domain's project
/// reads as not found, and a simulation saves nothing.
/// </summary>
public class ProtonFinanceInsightTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <summary>One tool call on its own question: the tool's result (its data, basis and datasets, or a refusal's status), and the reply.</summary>
    private async Task<(JsonNode Result, ProtonReplyResponse Reply)> CallAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here it is."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Tell me about its money" }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var reply = await response.ReadAsync<ProtonReplyResponse>();
        return (JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!, reply);
    }

    private async Task OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private static string Today => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static IEnumerable<(string Step, decimal Amount)> Bridge(JsonNode data)
        => data["bridge"]!.AsArray().Select(s => (s!["step"]!.GetValue<string>(), s["amount"]!.GetValue<decimal>()));

    private async Task<AuthResponse> DesignAdminAsync(AuthResponse superAdmin, params string[] codes)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        if (codes.Length > 0)
        {
            await OkAsync(Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
                new { changes = codes.Select(code => new { code, action = "grant", reason = "Handles the money" }) }, superAdmin.AccessToken));
        }

        return admin;
    }

    [Fact]
    public async Task A_super_admin_reads_a_design_projects_and_a_sites_profitability_and_where_it_comes_from()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // a Design project agreed at 5,000 USD (untaxed) with 1,000 of expenses
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await OkAsync(Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Structural survey", amount = 1_000m, currency = "USD", category = "other", spentOn = Today }, superAdmin.AccessToken));

        var (design, reply) = await CallAsync(superAdmin, "get_project_financial_summary", new { project_id = project.Id });
        var data = design["data"]!;
        (data["agreed_revenue"]!.GetValue<decimal>(), data["cost"]!["expenses"]!.GetValue<decimal>(), data["agreed_profit"]!.GetValue<decimal>())
            .ShouldBe((5000m, 1000m, 4000m));
        data["standing"]!.GetValue<string>().ShouldBe("fine");
        data["owed"]!.GetValue<decimal>().ShouldBe(5000m);
        Bridge(data).ShouldBe([("base_price", 5000m), ("expenses", -1000m)]);
        data["site_cash_profit"].ShouldBeNull();
        // the headline figures and the bridge, as datasets; the basis is shown under the answer
        var datasets = design["datasets"]!.AsArray();
        datasets.Count.ShouldBe(2);
        datasets.ShouldContain(d => d!["forms"]!.AsArray().Any(f => f!.GetValue<string>() == "kpis"));
        datasets.ShouldContain(d => d!["rows"]!.GetValue<int>() == 2 && d["forms"]!.AsArray().Any(f => f!.GetValue<string>() == "table"));
        design["basis"]!["rules"]!.ToJsonString().ShouldContain("profit floor");
        reply.Sources.ShouldHaveSingleItem().Label.ShouldBe($"Financial summary of {project.Title}");

        // a site worked at 1,000 USD with 600 of site expenses and 50 moved to the pocket money
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync(Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
            new { items = new[] { new { name = "Scaffolding", amount = 600m, spentOn = Today } } }, superAdmin.AccessToken));
        await OkAsync(Client.PostJsonAsync("/api/execution/pocket/contributions", new { projectId = site.Id, amount = 50m, note = "Set aside" }, superAdmin.AccessToken));

        var onSite = (await CallAsync(superAdmin, "get_project_financial_summary", new { project_id = site.Id })).Result["data"]!;
        (onSite["agreed_revenue"]!.GetValue<decimal>(), onSite["cost"]!["site_expenses"]!.GetValue<decimal>(), onSite["agreed_profit"]!.GetValue<decimal>())
            .ShouldBe((1000m, 600m, 350m));
        (onSite["to_pocket"]!.GetValue<decimal>(), onSite["site_cash_profit"]!.GetValue<decimal>()).ShouldBe((50m, -650m));
        Bridge(onSite).ShouldBe([("base_price", 1000m), ("site_expenses", -600m), ("pocket", -50m)]);
        onSite["departments"].ShouldBeNull();
    }

    [Fact]
    public async Task Without_financial_view_the_tools_are_refused_and_another_domains_project_reads_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        // an admin has no financial:view: the endpoint's policy keeps both tools from them, offered or asked for
        var plain = await DesignAdminAsync(superAdmin);
        var (refused, _) = await CallAsync(plain, "get_project_financial_summary", new { project_id = project.Id });
        refused["status"]!.GetValue<string>().ShouldBe("forbidden");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("get_project_financial_summary");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("simulate_project_financial_scenario");
        (await CallAsync(plain, "simulate_project_financial_scenario", new { project_id = project.Id, vo_amount = 500m }))
            .Result["status"]!.GetValue<string>().ShouldBe("forbidden");

        // with financial:view, a Design admin reads Design's project, and an Execution site reads as not found
        var viewer = await DesignAdminAsync(superAdmin, PermissionCodes.FinancialView);
        (await CallAsync(viewer, "get_project_financial_summary", new { project_id = project.Id }))
            .Result["data"]!["agreed_revenue"]!.GetValue<decimal>().ShouldBe(5000m);
        var (hidden, _) = await CallAsync(viewer, "get_project_financial_summary", new { project_id = site.Id });
        hidden["status"]!.GetValue<string>().ShouldBe("not_found");
        hidden.ToJsonString().ShouldNotContain(site.Title);
        (await CallAsync(viewer, "simulate_project_financial_scenario", new { project_id = site.Id, vo_amount = 500m }))
            .Result["status"]!.GetValue<string>().ShouldBe("not_found");
    }

    [Fact]
    public async Task A_scenario_shows_the_figures_before_and_after_a_variation_order_and_saves_nothing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var (scenario, reply) = await CallAsync(superAdmin, "simulate_project_financial_scenario", new { project_id = project.Id, vo_amount = 1000m });
        var data = scenario["data"]!;
        (data["before"]!["agreed_revenue"]!.GetValue<decimal>(), data["after"]!["agreed_revenue"]!.GetValue<decimal>()).ShouldBe((5000m, 6000m));
        data["agreed_profit_change"]!.GetValue<decimal>().ShouldBe(1000m);
        data["floor_would_refuse"]!.GetValue<bool>().ShouldBeFalse();
        data["after"]!["standing"]!.GetValue<string>().ShouldBe("fine");
        scenario["basis"]!["rules"]!.ToJsonString().ShouldContain("1,000 USD");
        scenario["datasets"]!.AsArray().ShouldHaveSingleItem()!["forms"]!.AsArray().Select(f => f!.GetValue<string>()).ShouldContain("kpis");
        reply.Sources.ShouldHaveSingleItem().Label.ShouldContain("Scenario on");

        // nothing was saved: the project's price is what it was
        var read = await (await Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        read.Money!.Revenue.ShouldBe(5000m);

        // an extra cost that takes the profit below zero: as a real change the floor would refuse it
        var costly = (await CallAsync(superAdmin, "simulate_project_financial_scenario",
            new { project_id = project.Id, extra_cost = 6000m, extra_cost_source = "expenses" })).Result["data"]!;
        (costly["after"]!["agreed_profit"]!.GetValue<decimal>(), costly["after"]!["standing"]!.GetValue<string>()).ShouldBe((-1000m, "at_zero"));
        costly["floor_would_refuse"]!.GetValue<bool>().ShouldBeTrue();

        // a site's VO is on its work price
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var onSite = (await CallAsync(superAdmin, "simulate_project_financial_scenario", new { project_id = site.Id, vo_amount = 500m })).Result["data"]!;
        (onSite["before"]!["agreed_revenue"]!.GetValue<decimal>(), onSite["after"]!["agreed_revenue"]!.GetValue<decimal>()).ShouldBe((1000m, 1500m));

        // a lever that doesn't apply is refused as the endpoint refuses it, and no lever at all is invalid
        var (supervision, _) = await CallAsync(superAdmin, "simulate_project_financial_scenario", new { project_id = project.Id, supervision_rate = 0.12m });
        supervision["status"]!.GetValue<string>().ShouldBe("refused");
        supervision["message"]!.GetValue<string>().ShouldContain("supervision rate");
        var (nothing, _) = await CallAsync(superAdmin, "simulate_project_financial_scenario", new { project_id = project.Id });
        nothing["status"]!.GetValue<string>().ShouldBe("invalid");
        nothing["message"]!.GetValue<string>().ShouldContain("at least one change");
    }

    /// <summary>A listed project's risks, found by its id: other tests' projects share the database.</summary>
    private static JsonArray? RisksOf(JsonNode data, long projectId)
        => data["projects"]!.AsArray().SingleOrDefault(p => p!["project_id"]!.GetValue<long>() == projectId)?["risks"]!.AsArray();

    [Fact]
    public async Task A_super_admin_reads_a_domains_risk_register_each_risk_with_its_fact_and_rule_and_no_internal_project()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // agreed at 1,000 USD with 950 of expenses: a margin of 5%, close to the floor
        var thin = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 1000m);
        await OkAsync(Client.PostJsonAsync($"/api/projects/{thin.Id}/expenses",
            new { label = "Facade study", amount = 950m, currency = "USD", category = "other", spentOn = Today }, superAdmin.AccessToken));
        // an expense that would take the profit to zero is refused and kept as a change waiting to be forced or dismissed
        var held = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 1000m);
        await (await Client.PostJsonAsync($"/api/projects/{held.Id}/expenses",
            new { label = "Model shop", amount = 1000m, currency = "USD", category = "other", spentOn = Today }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProfitFloor);
        // internal work is a cost centre: never a risk of loss, whatever it costs
        var created = await Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling " + Guid.NewGuid().ToString("N")[..6], domainCode = DomainCodes.Design, billingType = "internal" }, superAdmin.AccessToken);
        var studio = await created.ReadAsync<ProjectResponse>();
        await OkAsync(Client.PostJsonAsync($"/api/projects/{studio.Id}/expenses",
            new { label = "Plotter", amount = 500m, currency = "USD", category = "hardware", spentOn = Today }, superAdmin.AccessToken));

        var (design, reply) = await CallAsync(superAdmin, "detect_project_financial_risks", new { domain = "design", limit = 20 });
        var data = design["data"]!;
        data["domain"]!.GetValue<string>().ShouldBe(DomainCodes.Design);
        data["checked"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(2);
        var close = RisksOf(data, thin.Id).ShouldNotBeNull().ShouldHaveSingleItem()!;
        (close["kind"]!.GetValue<string>(), close["amount"]!.GetValue<decimal>()).ShouldBe(("profit_close", 50m));
        close["fact"]!.GetValue<string>().ShouldContain("50 USD on 1,000 USD agreed");
        close["rule"]!.GetValue<string>().ShouldContain("a margin of 10% or less, against the profit floor");
        var waiting = RisksOf(data, held.Id).ShouldNotBeNull().ShouldHaveSingleItem()!;
        waiting["kind"]!.GetValue<string>().ShouldBe("blocked_change");
        waiting["amount"].ShouldBeNull();
        RisksOf(data, studio.Id).ShouldBeNull();
        var counts = data["counts"]!.AsArray().ToDictionary(c => c!["kind"]!.GetValue<string>(), c => c!["projects"]!.GetValue<int>());
        counts["profit_close"].ShouldBeGreaterThanOrEqualTo(1);
        counts["blocked_change"].ShouldBeGreaterThanOrEqualTo(1);

        // the basis says how it was checked and what was left out; the register is a table, never a chart of mixed amounts
        design["basis"]!["rules"]!.ToJsonString().ShouldContain("a margin of 10% or less");
        design["basis"]!["omitted"]!.ToJsonString().ShouldContain("Internal projects");
        var datasets = design["datasets"]!.AsArray();
        datasets.ShouldContain(d => d!["title"]!.GetValue<string>() == "Design: risk register" && d["forms"]!.ToJsonString() == "[\"table\"]");
        datasets.ShouldContain(d => d!["title"]!.GetValue<string>() == "Design: projects by kind of risk");
        reply.Sources.ShouldHaveSingleItem().Label.ShouldBe("Design's financial risks");

        // a site that has spent more than its client paid: its cash profit is below zero
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync(Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
            new { items = new[] { new { name = "Scaffolding", amount = 100m, spentOn = Today } } }, superAdmin.AccessToken));
        var onSite = (await CallAsync(superAdmin, "detect_project_financial_risks", new { domain = "execution", limit = 20 })).Result["data"]!;
        var runway = RisksOf(onSite, site.Id).ShouldNotBeNull()[0]!;
        (runway["kind"]!.GetValue<string>(), runway["amount"]!.GetValue<decimal>()).ShouldBe(("short_runway", -100m));
        runway["fact"]!.GetValue<string>().ShouldContain("spent more than the client paid");
        runway["rule"]!.GetValue<string>().ShouldContain("already below 0");
    }

    [Fact]
    public async Task A_super_admin_reads_a_domains_spending_by_month_and_what_the_rules_call_unusual_with_no_person_in_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];

        // Design: the same licence entered twice on one project on the same day
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var licence = $"Licence {tag}";
        for (var i = 0; i < 2; i++)
        {
            await OkAsync(Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
                new { label = licence, amount = 100m, currency = "USD", category = "software", spentOn = Today }, superAdmin.AccessToken));
        }

        var (design, reply) = await CallAsync(superAdmin, "analyze_expense_trends", new { domain = "design" });
        var data = design["data"]!;
        var months = data["months"]!.AsArray();
        months.Count.ShouldBe(12);
        var thisMonth = months[months.Count - 1]!;
        thisMonth["so_far"]!.GetValue<bool>().ShouldBeTrue();
        thisMonth["categories"]!.AsArray().Single(c => c!["category"]!.GetValue<string>() == "software")!["usd"]!.GetValue<decimal>()
            .ShouldBeGreaterThanOrEqualTo(200m);
        var twice = data["anomalies"]!.AsArray().Single(a => a!["item"]?.GetValue<string>() == licence)!;
        (twice["kind"]!.GetValue<string>(), twice["project_id"]!.GetValue<long>(), twice["usd"]!.GetValue<decimal>()).ShouldBe(("same_amount_twice", project.Id, 100m));
        twice["expense_ids"]!.AsArray().Count.ShouldBe(2);
        twice["fact"]!.GetValue<string>().ShouldContain($"2 expenses of 100 USD for \"{licence}\"");
        twice["rule"]!.GetValue<string>().ShouldContain("maybe one typed twice");
        design["basis"]!["partial"]!.GetValue<bool>().ShouldBeTrue();
        design["basis"]!["rules"]!.ToJsonString().ShouldContain("Design expenses have no unit cost");
        var datasets = design["datasets"]!.AsArray();
        datasets.ShouldContain(d => d!["title"]!.GetValue<string>() == "Design: spending by category, by month" && d["forms"]!.ToJsonString().Contains("line"));
        datasets.ShouldContain(d => d!["title"]!.GetValue<string>() == "Design: spending the rules call unusual" && d["forms"]!.ToJsonString().Contains("table"));
        reply.Sources.ShouldHaveSingleItem().Label.ShouldBe("Design's spending by category");

        // fewer months on request; more than 24 is refused by the endpoint's own validator
        (await CallAsync(superAdmin, "analyze_expense_trends", new { domain = "design", months = 3 })).Result["data"]!["months"]!.AsArray().Count.ShouldBe(3);
        var (tooMany, _) = await CallAsync(superAdmin, "analyze_expense_trends", new { domain = "design", months = 30 });
        tooMany["status"]!.GetValue<string>().ShouldBe("invalid");
        tooMany["message"]!.GetValue<string>().ShouldContain("24");

        // a site's cement bought at 5 USD a bag three times, then at 8: a jump in its unit cost; the supplier is never shown
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var cement = $"Cement {tag}";
        const string supplier = "Georges Haddad";
        foreach (var (quantity, unitCost) in new[] { (10m, 5m), (11m, 5m), (12m, 5m), (10m, 8m) })
        {
            await OkAsync(Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
                new { items = new[] { new { name = cement, quantity, unit = "bag", unitCost, supplier, spentOn = Today } } }, superAdmin.AccessToken));
        }

        var (execution, _) = await CallAsync(superAdmin, "analyze_expense_trends", new { domain = "execution", months = 1 });
        var jump = execution["data"]!["anomalies"]!.AsArray().Single(a => a!["item"]?.GetValue<string>() == cement)!;
        (jump["kind"]!.GetValue<string>(), jump["project_id"]!.GetValue<long>(), jump["usd"]!.GetValue<decimal>(), jump["baseline"]!.GetValue<decimal>(),
                jump["pct_above"]!.GetValue<int>())
            .ShouldBe(("unit_cost_jump", site.Id, 80m, 5m, 60));
        jump["category"]!.GetValue<string>().ShouldBe("materials");
        jump["rule"]!.GetValue<string>().ShouldContain("25% or more above the average of its last 3 purchases");
        execution.ToJsonString().ShouldNotContain(supplier);
    }

    [Fact]
    public async Task Without_financial_view_a_domains_risks_and_spending_are_refused_and_another_domain_reads_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // an admin has no financial:view: the endpoints' policy keeps both tools from them, offered or asked for
        var plain = await DesignAdminAsync(superAdmin);
        (await CallAsync(plain, "detect_project_financial_risks", new { domain = "design" })).Result["status"]!.GetValue<string>().ShouldBe("forbidden");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("detect_project_financial_risks");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("analyze_expense_trends");
        (await CallAsync(plain, "analyze_expense_trends", new { domain = "design" })).Result["status"]!.GetValue<string>().ShouldBe("forbidden");

        // with financial:view, a Design admin's only business is chosen for them, and Execution reads as not found
        var viewer = await DesignAdminAsync(superAdmin, PermissionCodes.FinancialView);
        (await CallAsync(viewer, "detect_project_financial_risks", new { })).Result["data"]!["domain"]!.GetValue<string>().ShouldBe(DomainCodes.Design);
        (await CallAsync(viewer, "analyze_expense_trends", new { })).Result["data"]!["domain"]!.GetValue<string>().ShouldBe(DomainCodes.Design);
        (await CallAsync(viewer, "detect_project_financial_risks", new { domain = "execution" })).Result["status"]!.GetValue<string>().ShouldBe("not_found");
        (await CallAsync(viewer, "analyze_expense_trends", new { domain = "execution" })).Result["status"]!.GetValue<string>().ShouldBe("not_found");

        // someone who sees both businesses is asked which one, never given one silently
        var (which, _) = await CallAsync(superAdmin, "detect_project_financial_risks", new { });
        which["status"]!.GetValue<string>().ShouldBe("invalid");
        which["message"]!.GetValue<string>().ShouldContain("design or execution");
    }
}
