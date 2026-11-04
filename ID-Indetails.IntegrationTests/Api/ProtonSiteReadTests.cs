using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton reading an Execution site as its pages show it: its BOQs and VOs, ground labour, account and supervision taken,
/// the daily logs, the pocket money, and a project's invoices and quotes. Each needs its endpoint's own permission.
/// </summary>
public class ProtonSiteReadTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <summary>One tool call on its own question: its result, the data under "data" or a refusal's "status".</summary>
    private async Task<JsonNode> CallAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here it is."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Tell me about it" }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
    }

    private async Task OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private static DateOnly Monday()
    {
        var day = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    [Fact]
    public async Task A_sites_boqs_labour_and_account_are_read_as_its_pages_show_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, projectFee: 0);
        var boqSteps = new BoqTestSteps(_steps);
        await boqSteps.AgreeClientBoqAsync(superAdmin.AccessToken, site.Id, 1000m);
        var vo = await boqSteps.AgreeVoAsync(superAdmin.AccessToken, site.Id, 200m, "A second bathroom");

        var boqs = (await CallAsync(superAdmin, "list_boqs", new { project_id = site.Id }))["data"]!;
        var row = boqs["vos"]!.AsArray().Single(v => v!["id"]!.GetValue<long>() == vo.Id)!;
        (row["status"]!.GetValue<string>(), row["total_usd"]!.GetValue<decimal>(), row["reason"]!.GetValue<string>()).ShouldBe(("agreed", 200m, "A second bathroom"));
        boqs["price"]!["agreed_vos_usd"]!.GetValue<decimal>().ShouldBe(200m);
        var lines = (await CallAsync(superAdmin, "get_boq", new { boq_id = vo.Id }))["data"]!["lines_shown"]!.AsArray();
        lines.ShouldHaveSingleItem()!["description"]!.GetValue<string>().ShouldBe("Change");

        // a week reported on the site: asked for by any of its days, it reads from its Monday
        var monday = Monday();
        await OkAsync(Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{monday:yyyy-MM-dd}",
            new { days = new[] { new { date = monday.ToString("yyyy-MM-dd"), workers = 3, dailyPay = 25m } } }, superAdmin.AccessToken));
        var labour = (await CallAsync(superAdmin, "get_labour", new { project_id = site.Id, week = monday.AddDays(2).ToString("yyyy-MM-dd") }))["data"]!;
        labour["week"]!["week_start"]!.GetValue<string>().ShouldBe(monday.ToString("yyyy-MM-dd"));
        labour["week"]!["total"]!.GetValue<decimal>().ShouldBe(75m);
        labour["weeks"]!.AsArray().First()!["reported"]!.GetValue<bool>().ShouldBeTrue();

        // supervision taken shows in the account, with each time it was taken (test sites start with no supervision rate)
        await _steps.ExecuteAsync("UPDATE projects SET supervision_rate = 0.1 WHERE id = @id", ("@id", site.Id));
        await OkAsync(Client.PostJsonAsync($"/api/projects/{site.Id}/supervision-withdrawals",
            new { amountUsd = 20m, takenOn = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM-dd"), note = "First part" }, superAdmin.AccessToken));
        var account = (await CallAsync(superAdmin, "get_site_account", new { project_id = site.Id }))["data"]!;
        account["supervision"]!["taken_usd"]!.GetValue<decimal>().ShouldBe(20m);
        account["supervision_taken"]!.AsArray().ShouldHaveSingleItem()!["note"]!.GetValue<string>().ShouldBe("First part");

        // the account is money: an Execution admin without financial:view doesn't get it, though they see the labour
        (await CallAsync(siteAdmin, "get_site_account", new { project_id = site.Id }))["status"]!.GetValue<string>().ShouldBe("forbidden");
        (await CallAsync(siteAdmin, "get_labour", new { project_id = site.Id }))["data"]!["total"]!.GetValue<decimal>().ShouldBe(75m);
    }

    [Fact]
    public async Task Daily_logs_are_read_by_their_writer_and_by_whoever_sees_everyones()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id]);
        await OkAsync(Client.PostJsonAsync($"/api/projects/{site.Id}/site-logs", new { body = "Poured the ground floor slab" }, rami.AccessToken));

        (await CallAsync(rami, "list_my_sites", new { }))["data"]!.AsArray().Select(s => s!["id"]!.GetValue<long>()).ShouldContain(site.Id);
        var mine = (await CallAsync(rami, "list_my_site_logs", new { project_id = site.Id }))["data"]!["logs"]!.AsArray();
        mine.ShouldHaveSingleItem()!["text"]!.GetValue<string>().ShouldBe("Poured the ground floor slab");

        var everyone = (await CallAsync(superAdmin, "list_site_logs", new { project_id = site.Id }))["data"]!["logs"]!.AsArray().ShouldHaveSingleItem()!;
        everyone["person"]!.GetValue<string>().ShouldBe($"{rami.User.FirstName} {rami.User.LastName}");
        everyone["site"]!["id"]!.GetValue<long>().ShouldBe(site.Id);

        // Design has no sites: the daily logs aren't offered there, and a site's staff can't read everyone's
        (await CallAsync(designAdmin, "list_site_logs", new { project_id = site.Id }))["status"]!.GetValue<string>().ShouldBe("forbidden");
        (await CallAsync(rami, "list_site_logs", new { project_id = site.Id }))["status"]!.GetValue<string>().ShouldBe("forbidden");
    }

    [Fact]
    public async Task The_pocket_money_a_sites_contribution_an_invoices_lines_and_a_projects_quotes_are_read()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // a site agreed at 1,000 USD with 400 of profit gives the pocket money 50
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync(Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
            new { items = new[] { new { name = "Scaffolding", amount = 600m, spentOn = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM-dd") } } },
            superAdmin.AccessToken));
        await OkAsync(Client.PostJsonAsync("/api/execution/pocket/contributions", new { projectId = site.Id, amount = 50m, note = "Set aside" }, superAdmin.AccessToken));
        var pocket = (await CallAsync(superAdmin, "get_pocket_money", new { project_id = site.Id }))["data"]!;
        var moved = pocket["recent"]!.AsArray().ShouldHaveSingleItem()!;
        (moved["kind"]!.GetValue<string>(), moved["amount"]!.GetValue<decimal>(), moved["note"]!.GetValue<string>()).ShouldBe(("contribution", 50m, "Set aside"));

        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var invoice = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Concept design", amount = 2000m } }, termsDays = 15, notes = "Thank you" }, superAdmin.AccessToken))
            .ReadAsync<InvoiceResponse>();
        var read = (await CallAsync(superAdmin, "get_invoice", new { invoice_id = invoice.Id }))["data"]!;
        read["lines"]!.AsArray().ShouldHaveSingleItem()!["description"]!.GetValue<string>().ShouldBe("Concept design");
        (read["terms_days"]!.GetValue<int>(), read["notes"]!.GetValue<string>()).ShouldBe((15, "Thank you"));
    }
}
