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
}
