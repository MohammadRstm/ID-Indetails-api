using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Reports;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Proton's analytical tools end to end, with a scripted model: their figures match the existing reports, they carry
/// the user's permissions, and their limits hold.
/// </summary>
public class ProtonAnalysisTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private Task<HttpResponseMessage> AskAsync(AuthResponse who, string text, Guid? conversationId = null)
        => Client.PostJsonAsync("/api/proton/messages", new { conversationId, text }, who.AccessToken);

    private static async Task<ProtonReplyResponse> OkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonReplyResponse>();
    }

    /// <summary>The tool result sent back in request <paramref name="request"/>, parsed.</summary>
    private JsonNode Result(int request) => JsonNode.Parse(api.Anthropic.ToolResults(request).Single()["content"]!.GetValue<string>())!;

    private static JsonNode Row(JsonNode result, long id)
        => result["data"]!["rows"]!.AsArray().Single(r => r!["id"]!.GetValue<long>() == id)!;

    [Fact]
    public async Task Compare_work_matches_the_work_report_and_shows_who_drove_the_change()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var brand = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var web = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        // 9–15 Feb 2026 against 2–8 Feb: brand 3 h against 1 h, web none against 1 h.
        await Work.InsertAsync(anna.User.Id, brand.Id, "2026-02-10 07:00:00", "2026-02-10 09:00:00");
        await Work.InsertAsync(ben.User.Id, brand.Id, "2026-02-11 07:00:00", "2026-02-11 08:00:00");
        await Work.InsertAsync(anna.User.Id, brand.Id, "2026-02-03 07:00:00", "2026-02-03 08:00:00");
        await Work.InsertAsync(anna.User.Id, web.Id, "2026-02-04 07:00:00", "2026-02-04 08:00:00");

        var period = new { period = "custom", from = "2026-02-09", to = "2026-02-15" };
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("compare_work", new { period.period, period.from, period.to, group_by = "project", limit = 15 }),
            FakeAnthropic.ToolUse("compare_work", new { period.period, period.from, period.to, group_by = "person", project_id = brand.Id }),
            FakeAnthropic.Text("Brand rose by 2.0 h."));

        var reply = await OkAsync(AskAsync(superAdmin, "How did hours change last week?"));

        var byProject = Result(1);
        byProject["basis"]!["compared_with"]!["from"]!.GetValue<string>().ShouldBe("2026-02-02");
        var brandRow = Row(byProject, brand.Id)["value"]!;
        brandRow["current"]!.GetValue<decimal>().ShouldBe(3m);
        brandRow["previous"]!.GetValue<decimal>().ShouldBe(1m);
        brandRow["diff"]!.GetValue<decimal>().ShouldBe(2m);
        brandRow["pct"]!.GetValue<int>().ShouldBe(200);
        var webRow = Row(byProject, web.Id)["value"]!;
        webRow["current"]!.GetValue<decimal>().ShouldBe(0m);
        webRow["previous"]!.GetValue<decimal>().ShouldBe(1m);

        // The same figure as the work report itself.
        var report = await (await Client.GetAsync("/api/reports/work?from=2026-02-09&to=2026-02-15", superAdmin.AccessToken)).ReadAsync<WorkReportResponse>();
        report.ByProject.Single(p => p.ProjectId == brand.Id).Minutes.ShouldBe(180);

        // Within the project, by person: each contributed half of the change; Ben had nothing before.
        var byPerson = Result(2);
        Row(byPerson, anna.User.Id)["share_of_change_pct"]!.GetValue<int>().ShouldBe(50);
        Row(byPerson, ben.User.Id)["value"]!["pct"].ShouldBeNull();
        byPerson["basis"]!["rules"]!.ToJsonString().ShouldContain("30%");

        var source = reply.Sources.ShouldHaveSingleItem();
        source.Label.ShouldBe("Hours compared");
        source.Period.ShouldBe("9–15 Feb 2026");
        source.ComparedWith.ShouldBe("2–8 Feb 2026");
        source.Partial.ShouldBeFalse();
    }

    [Fact]
    public async Task Compare_money_matches_the_financials_summary()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Render farm", amount = 1_500m, currency = "USD", category = "software", spentOn = "2026-03-10" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("compare_money", new { domain = "design", period = "custom", from = "2026-03-01", to = "2026-03-31", group_by = "project" }),
            FakeAnthropic.Text("Costs rose."));

        await OkAsync(AskAsync(superAdmin, "How did Design's money change in March?"));

        var summary = await (await Client.GetAsync("/api/finance/design/summary?from=2026-03-01&to=2026-03-31", superAdmin.AccessToken))
            .ReadAsync<FinanceSummaryResponse>();
        var data = Result(1)["data"]!;
        data["cost"]!["current"]!.GetValue<decimal>().ShouldBe(summary.Cost.Total);
        data["received"]!["current"]!.GetValue<decimal>().ShouldBe(summary.Revenue);
        data["cost"]!["previous"]!.GetValue<decimal>().ShouldBe(0m);
        var row = data["projects"]!.AsArray().Single(p => p!["project_id"]!.GetValue<long>() == project.Id)!;
        row["cost"]!["current"]!.GetValue<decimal>().ShouldBe(1_500m);
        row["cost_notable"]!.GetValue<bool>().ShouldBeTrue();
        data.ToJsonString().ShouldNotContain("labour_by_person");
    }

    /// <summary>Regression: without a domain, compare_money quietly used Design, whatever the user sees.</summary>
    [Fact]
    public async Task Compare_money_without_a_domain_asks_unless_the_user_sees_one_business()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, executionMoney) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        api.Anthropic.Reply(FakeAnthropic.ToolUse("compare_money", new { }), FakeAnthropic.Text("Which business?"));

        await OkAsync(AskAsync(superAdmin, "How is our money this month?"));
        var both = Result(1);
        both["status"]!.GetValue<string>().ShouldBe("invalid");
        both["message"]!.GetValue<string>().ShouldContain("Which business");

        // someone who sees only Execution gets Execution, not Design
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("compare_money", new { }), FakeAnthropic.Text("Here it is."));
        var reply = await OkAsync(AskAsync(executionMoney, "How is our money this month?"));
        Result(1)["status"]!.GetValue<string>().ShouldBe("ok");
        reply.Sources.ShouldHaveSingleItem().Label.ShouldStartWith("Execution");
    }

    [Fact]
    public async Task A_summary_holds_only_what_the_user_may_see_and_says_what_it_left_out()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_company_summary", new { period = "this_week" }),
            FakeAnthropic.ToolUse("compare_money", new { domain = "design" }),
            FakeAnthropic.Text("Here is the week."));

        var reply = await OkAsync(AskAsync(admin, "Give me a summary of the week"));

        api.Anthropic.ToolsOffered(0).ShouldNotContain("compare_money");
        var summary = Result(1);
        var domains = summary["data"]!["domains"]!.AsArray();
        domains.Select(d => d!["domain"]!.GetValue<string>()).ShouldBe([DomainCodes.Design]);
        domains[0]!["money"].ShouldBeNull();
        domains[0]!["work"].ShouldNotBeNull();
        summary["data"]!["company"].ShouldBeNull();
        summary["basis"]!["omitted"]!.ToJsonString().ShouldContain("financial:view");
        // Regression: the overhead was left out without a word, so the summary read as complete
        summary["basis"]!["omitted"]!.ToJsonString().ShouldContain("overhead");
        Result(2)["status"]!.GetValue<string>().ShouldBe("forbidden");
        reply.Sources.ShouldHaveSingleItem().Omitted.ShouldContain(o => o.Contains("financial:view"));

        // The super admin's summary covers both domains, their money and the company.
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("get_company_summary", new { period = "this_week" }), FakeAnthropic.Text("Here is the week."));
        await OkAsync(AskAsync(superAdmin, "Give me a summary of the week"));
        var full = Result(1)["data"]!;
        full["domains"]!.AsArray().Count.ShouldBe(2);
        full["domains"]!.AsArray().ShouldAllBe(d => d!["money"] != null);
        full["company"].ShouldNotBeNull();
        full.ToJsonString().ShouldNotContain("salar", Case.Insensitive);
        (Result(1)["basis"]!["omitted"]?.ToJsonString() ?? "").ShouldNotContain("overhead");
    }

    /// <summary>
    /// Regression: the money follow-ups stop at the dashboard's 6 most urgent, and attention read as if that were
    /// every money problem there is.
    /// </summary>
    [Fact]
    public async Task Attention_says_when_the_money_follow_ups_were_capped()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        // created by someone who can't set money, so none has a payment agreed: seven follow-ups, one over the cap
        for (var i = 0; i < 7; i++)
        {
            await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        }

        api.Anthropic.Reply(FakeAnthropic.ToolUse("get_attention", new { domain = "design", limit = 10 }), FakeAnthropic.Text("Several things."));
        await OkAsync(AskAsync(superAdmin, "Is there anything I should look at?"));

        Result(1)["basis"]!["omitted"]!.ToJsonString().ShouldContain("most urgent");
    }

    [Fact]
    public async Task Attention_lists_each_project_once_with_its_facts_and_rules()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var late = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var paused = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var target = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20).ToString("yyyy-MM-dd");
        await _steps.ExecuteAsync("UPDATE projects SET start_date = DATE_SUB(@d, INTERVAL 30 DAY), target_date = @d WHERE id = @id", ("@d", target), ("@id", late.Id));
        (await Client.PostEmptyAsync($"/api/projects/{paused.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("get_attention", new { domain = "design", limit = 10 }), FakeAnthropic.Text("Two things."));

        await OkAsync(AskAsync(superAdmin, "Is there anything I should look at?"));

        var items = Result(1)["data"]!["items"]!.AsArray();
        var lateItem = items.Single(i => i!["project"]?["id"]?.GetValue<long>() == late.Id)!;
        var kinds = lateItem["signals"]!.AsArray().Select(s => s!["kind"]!.GetValue<string>()).ToList();
        kinds[0].ShouldBe("project_overdue");
        kinds.ShouldContain("idle");
        lateItem["signals"]![0]!["fact"]!.GetValue<string>().ShouldContain(target);
        items.Count(i => i!["project"]?["id"]?.GetValue<long>() == late.Id).ShouldBe(1);
        items.Single(i => i!["project"]?["id"]?.GetValue<long>() == paused.Id)!["signals"]![0]!["kind"]!.GetValue<string>().ShouldBe("on_hold");
        // overdue is ranked before on hold
        items.ToList().FindIndex(i => i!["project"]?["id"]?.GetValue<long>() == late.Id)
            .ShouldBeLessThan(items.ToList().FindIndex(i => i!["project"]?["id"]?.GetValue<long>() == paused.Id));
    }

    /// <summary>
    /// Regression: the feed was read newest first, three pages of 40, and filtered to the period afterwards, so an
    /// older period behind more than 120 newer events came back empty.
    /// </summary>
    [Fact]
    public async Task Recent_activity_reads_an_older_period_whatever_happened_since()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await _steps.ScalarAsync<long>(
            "SELECT id FROM activity_events WHERE project_id = @p ORDER BY id LIMIT 1", ("@p", project.Id));
        // the creation happened 40 days ago; 125 events on the same project happened since
        await _steps.ExecuteAsync("UPDATE activity_events SET occurred_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 40 DAY) WHERE id = @id", ("@id", created));
        await _steps.ExecuteAsync(
            "INSERT INTO activity_events (domain_code, kind, occurred_at, actor_id, project_id, client_id, subject_type, subject_id, amount, prev_amount, payload, is_demo) " +
            "SELECT e.domain_code, e.kind, UTC_TIMESTAMP(3), e.actor_id, e.project_id, e.client_id, e.subject_type, e.subject_id, e.amount, e.prev_amount, e.payload, e.is_demo " +
            "FROM activity_events e CROSS JOIN (SELECT 1 FROM information_schema.columns LIMIT 125) n WHERE e.id = @id", ("@id", created));
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-40));
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_recent_activity", new
            {
                project_id = project.Id, period = "custom", from = day.AddDays(-1).ToString("yyyy-MM-dd"), to = day.AddDays(1).ToString("yyyy-MM-dd"),
            }),
            FakeAnthropic.Text("It was created then."));

        await OkAsync(AskAsync(superAdmin, "What happened on that project 40 days ago?"));

        var data = Result(1)["data"]!;
        data["events"]!.AsArray().Count.ShouldBe(1);
        data["total"]!.GetValue<int>().ShouldBe(1);
        data["more_than_shown"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Changes_are_counted_from_the_activity_history_without_pay()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{anna.User.Id}/pay",
            new { basis = "monthly", amount = 6400m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("summarize_changes", new { period = "last7_days" }), FakeAnthropic.Text("A few changes."));

        await OkAsync(AskAsync(superAdmin, "What changed this week?"));

        var result = Result(1);
        result["status"]!.GetValue<string>().ShouldBe("ok", result.ToJsonString());
        var data = result["data"]!;
        data["project_changes"]!.AsArray()
            .ShouldContain(c => c!["kind"]!.GetValue<string>() == "project_hold" && c["project"]!["id"]!.GetValue<long>() == project.Id);
        data["by_kind"]!.ToJsonString().ShouldContain("project_created");
        data["by_kind"]!.ToJsonString().ShouldNotContain("pay_changed");
        data["events"]!["previous"].ShouldNotBeNull();

        // The feed's new date filter, as the API exposes it.
        var today = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);
        var feed = await Client.GetAsync($"/api/activity?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&projectId={project.Id}", superAdmin.AccessToken);
        (await feed.Content.ReadAsStringAsync()).ShouldContain("project_hold");
        (await (await Client.GetAsync($"/api/activity?from={today.AddDays(1):yyyy-MM-dd}&projectId={project.Id}", superAdmin.AccessToken)).Content.ReadAsStringAsync())
            .ShouldContain("\"totalCount\":0");
        await (await Client.GetAsync($"/api/activity?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}", superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Heavy_analyses_are_limited_per_question()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_attention", new { }),
            FakeAnthropic.ToolUse("get_attention", new { domain = "design" }),
            FakeAnthropic.ToolUse("get_company_summary", new { }),
            FakeAnthropic.ToolUse("compare_money", new { domain = "design" }),
            FakeAnthropic.ToolUse("get_attention", new { domain = "execution" }),
            FakeAnthropic.Text("That's what I could check."));

        await OkAsync(AskAsync(superAdmin, "Look at everything"));

        for (var request = 1; request <= 4; request++)
        {
            Result(request)["status"]!.GetValue<string>().ShouldBe("ok");
        }

        var fifth = Result(5);
        fifth["status"]!.GetValue<string>().ShouldBe("refused");
        fifth["message"]!.GetValue<string>().ShouldContain("4 larger analyses");
    }

    [Fact]
    public async Task A_follow_up_keeps_the_comparison_and_a_long_conversation_rolls_over_with_its_focus()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("compare_work", new { period = "custom", from = "2026-02-09", to = "2026-02-15", group_by = "total" }),
            FakeAnthropic.Text("Hours were flat."),
            FakeAnthropic.Text("Nobody stood out.", inputTokens: 90_000),
            FakeAnthropic.Text("February had about the same hours."));

        var first = await OkAsync(AskAsync(superAdmin, "How did hours change?"));
        await OkAsync(AskAsync(superAdmin, "Which project contributed most?", first.ConversationId));

        var context = api.Anthropic.Requests[2]["messages"]!.AsArray().Last()!.ToJsonString();
        context.ShouldContain("compared with 2026-02-02 to 2026-02-08");
        context.ShouldContain("last analysis: Hours compared");

        // The last request sent 90,000 input tokens, past the 80,000 cap: the chat carries on under the same id from a
        // recap written by the app, with what the tools found still in focus.
        var third = await OkAsync(AskAsync(superAdmin, "And the month before?", first.ConversationId));
        third.ConversationId.ShouldBe(first.ConversationId);
        third.RolledOver.ShouldBeTrue();
        first.RolledOver.ShouldBeFalse();

        var messages = api.Anthropic.Requests[3]["messages"]!.AsArray();
        messages.Count.ShouldBe(1);
        var fresh = string.Join("\n", messages[0]!["content"]!.AsArray().Select(b => b!["text"]?.GetValue<string>()));
        fresh.ShouldContain("compared with 2026-02-02 to 2026-02-08");
        fresh.ShouldContain("a recap by the app");
        fresh.ShouldContain("The user asked: \"How did hours change?\" You answered: \"Hours were flat.\"");
        fresh.ShouldContain("And the month before?");
    }
}
