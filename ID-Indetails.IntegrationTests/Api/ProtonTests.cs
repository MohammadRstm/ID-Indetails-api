using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Proton.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Proton end to end, with a scripted model: who may ask, what the tools may see, and what is recorded.</summary>
public class ProtonTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private Task<HttpResponseMessage> AskAsync(AuthResponse who, string text, Guid? conversationId = null)
        => Client.PostJsonAsync("/api/proton/messages", new { conversationId, text }, who.AccessToken);

    private static async Task<ProtonReplyResponse> OkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonReplyResponse>();
    }

    [Fact]
    public async Task Admins_and_super_admins_may_ask_employees_may_not_and_only_super_admins_see_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await AskAsync(employee, "What projects are active?")).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/proton/usage", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var usage = await Client.GetAsync("/api/proton/usage", admin.AccessToken);
        var usageBody = await usage.Content.ReadAsStringAsync();
        usage.StatusCode.ShouldBe(HttpStatusCode.OK, usageBody);
        usageBody.ShouldContain("\"questionsAllowed\":300");
        usageBody.ShouldNotContain("usd", Case.Insensitive);
        usageBody.ShouldNotContain("cost", Case.Insensitive);

        await (await Client.GetAsync("/api/proton/budget", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/proton/budget/top-ups", new { amountUsd = 5m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await Client.GetAsync("/api/proton/budget", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_question_runs_the_tools_answers_from_their_results_and_is_recorded()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("list_projects", new { status = "active" }),
            FakeAnthropic.Text($"You have one active project: {project.Title}."));

        var reply = await OkAsync(AskAsync(superAdmin, "Which projects are active?"));

        reply.Reply.ShouldBe($"You have one active project: {project.Title}.");
        reply.ConversationId.ShouldNotBe(Guid.Empty);
        reply.Limited.ShouldBeFalse();
        reply.Refs.ShouldContain(r => r.Type == ProtonRefType.Project && r.Id == project.Id);
        reply.Usage.QuestionsUsed.ShouldBe(1);

        // First call: the static prompt, the tools this super admin may use, and the question after Hourline's context note.
        var first = api.Anthropic.Requests[0];
        first["system"]!.ToJsonString().ShouldContain("You are Proton");
        api.Anthropic.ToolsOffered(0).ShouldContain("list_projects");
        api.Anthropic.ToolsOffered(0).ShouldContain("get_company_overview");
        var question = first["messages"]!.AsArray().Last()!.ToJsonString();
        question.ShouldContain("Context from Hourline");
        question.ShouldContain("Which projects are active?");

        // Second call: the project list, read through the projects service.
        var result = api.Anthropic.ToolResults(1).ShouldHaveSingleItem();
        result.ToJsonString().ShouldContain(project.Title);
        result["content"]!.GetValue<string>().ShouldStartWith("{\"status\":\"ok\"");

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        var turn = await db.ProtonUsage.SingleAsync(u => u.UserId == superAdmin.User.Id);
        turn.Outcome.ShouldBe(ID_Indetails.api.Enums.ProtonOutcome.Answered);
        turn.ConversationId.ShouldBe(reply.ConversationId);
        turn.RoundTrips.ShouldBe(2);
        turn.ToolCalls.ShouldBe(1);
        turn.Question.ShouldBe("Which projects are active?");
        turn.Answer.ShouldBe(reply.Reply);
        turn.Tools.ShouldContain("list_projects");
        turn.CostUsd.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task The_usage_record_keeps_tool_inputs_as_a_summary_never_their_free_text()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("find", new { text = "Villa of Mr Haddad near the old souk", types = new[] { "project" } }),
            FakeAnthropic.Text("I couldn't find it."));

        await OkAsync(AskAsync(superAdmin, "Find the villa of Mr Haddad near the old souk"));

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        var tools = (await db.ProtonUsage.Where(u => u.UserId == superAdmin.User.Id).OrderByDescending(u => u.Id).FirstAsync()).Tools;
        tools.ShouldNotContain("Haddad");
        var input = System.Text.Json.Nodes.JsonNode.Parse(tools)!.AsArray().Single()!["input"]!;
        input["text"]!.GetValue<string>().ShouldBe("text(36)");
        input["types"]!.GetValue<string>().ShouldBe("list(1)");
    }

    [Fact]
    public async Task Every_block_of_a_response_goes_back_to_the_model_unchanged_and_an_unknown_one_fails_the_question()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(
            FakeAnthropic.Blocks("tool_use",
                new { type = "thinking", thinking = "Find the right tool.", signature = "sig-1" },
                new { type = "server_tool_use", id = "srvtoolu_1", name = "tool_search_tool_bm25", input = new { query = "projects" } },
                new
                {
                    type = "tool_search_tool_result", tool_use_id = "srvtoolu_1",
                    content = new { type = "tool_search_tool_search_result", tool_references = new[] { new { type = "tool_reference", tool_name = "list_projects" } } },
                },
                new { type = "tool_use", id = "toolu_9", name = "list_projects", input = new { status = "active" } }),
            FakeAnthropic.Text("Here they are."));

        await OkAsync(AskAsync(superAdmin, "Which projects are active?"));

        var echoed = api.Anthropic.Requests[1]["messages"]!.AsArray()[^2]!;
        echoed["role"]!.GetValue<string>().ShouldBe("assistant");
        var blocks = echoed["content"]!.AsArray();
        blocks.Select(b => b!["type"]!.GetValue<string>()).ShouldBe(["thinking", "server_tool_use", "tool_search_tool_result", "tool_use"]);
        blocks[0]!["signature"]!.GetValue<string>().ShouldBe("sig-1");
        blocks[1]!["id"]!.GetValue<string>().ShouldBe("srvtoolu_1");
        blocks[1]!["input"]!["query"]!.GetValue<string>().ShouldBe("projects");
        blocks[2]!["tool_use_id"]!.GetValue<string>().ShouldBe("srvtoolu_1");
        blocks[2]!["content"]!["tool_references"]![0]!["tool_name"]!.GetValue<string>().ShouldBe("list_projects");
        blocks[3]!["id"]!.GetValue<string>().ShouldBe("toolu_9");

        // A kind Proton can't send back would break the conversation: the question fails instead of dropping it.
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.Blocks("end_turn",
            new { type = "container_upload", file_id = "file_1" },
            new { type = "text", text = "Done." }));
        await (await AskAsync(superAdmin, "Anything else?")).ShouldFailWithAsync(503, ErrorCodes.ProtonUnavailable);
    }

    [Fact]
    public async Task A_follow_up_continues_the_conversation_with_what_it_is_about()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_project", new { project_id = project.Id }),
            FakeAnthropic.Text($"{project.Title} is active."),
            FakeAnthropic.Text("Nobody has logged hours on it yet."));

        var first = await OkAsync(AskAsync(superAdmin, $"Tell me about {project.Title}"));
        var second = await OkAsync(AskAsync(superAdmin, "Who worked on it?", first.ConversationId));

        second.ConversationId.ShouldBe(first.ConversationId);
        var followUp = api.Anthropic.Requests[2]["messages"]!.AsArray();
        // Question, tool call, tool result, answer — then the follow-up, which knows the project being discussed.
        followUp.Count.ShouldBe(5);
        followUp.Last()!.ToJsonString().ShouldContain($"project #{project.Id}");
        followUp.Last()!.ToJsonString().ShouldContain("Who worked on it?");
    }

    [Fact]
    public async Task Tools_carry_the_users_own_authorization_so_other_domains_and_company_figures_stay_out_of_reach()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_company_overview", new { period = "this_month" }),
            FakeAnthropic.ToolUse("get_project", new { project_id = site.Id }),
            FakeAnthropic.Text("I couldn't find that."));

        await OkAsync(AskAsync(designAdmin, "How is the company doing, and the site?"));

        var offered = api.Anthropic.ToolsOffered(0);
        offered.ShouldContain("list_work_entries");
        offered.ShouldNotContain("get_domain_finance");   // financial:view is the super admin's too
        offered.ShouldNotContain("get_company_overview"); // company:view is the super admin's
        offered.ShouldNotContain("get_site_summary");     // an Execution-only tool for a Design admin

        // Asked for anyway, the toolbox refuses it…
        var refused = api.Anthropic.ToolResults(1).ShouldHaveSingleItem();
        refused["is_error"]!.GetValue<bool>().ShouldBeTrue();
        refused.ToJsonString().ShouldContain("forbidden");
        // …and another domain's project reads as not found, exactly as GET /api/projects/{id} does.
        var hidden = api.Anthropic.ToolResults(2).ShouldHaveSingleItem();
        hidden.ToJsonString().ShouldContain("not_found");
        hidden.ToJsonString().ShouldNotContain(site.Title);
    }

    [Fact]
    public async Task Someone_elses_conversation_cannot_be_continued()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        api.Anthropic.Reply(FakeAnthropic.Text("Hello."));
        var mine = await OkAsync(AskAsync(admin, "Hi"));

        await (await AskAsync(superAdmin, "What did we talk about?", mine.ConversationId))
            .ShouldFailWithAsync(404, ErrorCodes.ProtonConversationEnded);
        (await Client.DeleteAsync($"/api/proton/conversations/{mine.ConversationId}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await AskAsync(admin, "Still there?", mine.ConversationId)).ShouldFailWithAsync(404, ErrorCodes.ProtonConversationEnded);
    }

    [Fact]
    public async Task Bad_tool_input_goes_back_to_the_model_and_a_question_needs_text()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("list_projects", new { bogus = 1 }),
            FakeAnthropic.ToolUse("list_meetings", new { period = "custom", from = "2026-01-01", to = "2026-12-31" }),
            FakeAnthropic.Text("Could you narrow that down?"));

        await OkAsync(AskAsync(superAdmin, "Projects?"));

        api.Anthropic.ToolResults(1).ShouldHaveSingleItem().ToJsonString().ShouldContain("invalid");
        api.Anthropic.ToolResults(2).ShouldHaveSingleItem().ToJsonString().ShouldContain("92 days");
        await (await AskAsync(superAdmin, "")).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task When_the_model_fails_the_user_gets_a_safe_error_and_the_turn_is_still_recorded()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Fail(HttpStatusCode.InternalServerError);

        var error = await (await AskAsync(superAdmin, "Anything new?")).ShouldFailWithAsync(503, ErrorCodes.ProtonUnavailable);
        error.Message.ShouldNotContain("boom");

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        (await db.ProtonUsage.SingleAsync(u => u.UserId == superAdmin.User.Id)).Outcome.ShouldBe(ID_Indetails.api.Enums.ProtonOutcome.Failed);
    }

    [Fact]
    public async Task While_a_tool_runs_the_database_refuses_every_write()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();

        await Should.ThrowAsync<ProtonWriteRefusedException>(() => ProtonReadOnly.RunAsync(
            () => db.Database.ExecuteSqlRawAsync("UPDATE domains SET name = name")));
        (await ProtonReadOnly.RunAsync(() => db.Domains.CountAsync())).ShouldBe(2);
        (await db.Database.ExecuteSqlRawAsync("UPDATE domains SET name = name")).ShouldBeGreaterThanOrEqualTo(0);
    }
}

/// <summary>Its own factory: the company budget starts at zero and each person may ask twice a month.</summary>
public class ProtonLimitTests(ProtonLimitsApiFactory api) : IClassFixture<ProtonLimitsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Proton_stops_at_the_company_budget_until_a_super_admin_tops_it_up_and_at_each_persons_allowance()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        Task<HttpResponseMessage> Ask() => Client.PostJsonAsync("/api/proton/messages", new { text = "Hi" }, admin.AccessToken);

        await (await Ask()).ShouldFailWithAsync(422, ErrorCodes.ProtonBudgetUsed);

        await (await Client.PostJsonAsync("/api/proton/budget/top-ups", new { amountUsd = 0m }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var topUp = await Client.PostJsonAsync("/api/proton/budget/top-ups", new { amountUsd = 5m, note = "October" }, superAdmin.AccessToken);
        topUp.StatusCode.ShouldBe(HttpStatusCode.Created, await topUp.Content.ReadAsStringAsync());
        var budget = await topUp.ReadAsync<ProtonBudgetResponse>();
        budget.BudgetUsd.ShouldBe(5m);
        budget.TopUps.ShouldHaveSingleItem().Note.ShouldBe("October");

        api.Anthropic.Reply(FakeAnthropic.Text("Hello."), FakeAnthropic.Text("Hello again."));
        (await Ask()).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Ask()).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await Ask()).ShouldFailWithAsync(422, ErrorCodes.ProtonAllowanceUsed);

        var usage = await (await Client.GetAsync("/api/proton/usage", admin.AccessToken)).ReadAsync<ProtonUsageResponse>();
        usage.QuestionsUsed.ShouldBe(2);
        usage.QuestionsRemaining.ShouldBe(0);
        usage.Available.ShouldBeFalse();

        var spent = await (await Client.GetAsync("/api/proton/budget", superAdmin.AccessToken)).ReadAsync<ProtonBudgetResponse>();
        spent.Questions.ShouldBe(2);
        spent.SpentUsd.ShouldBeGreaterThan(0m);
        spent.ByUser.ShouldHaveSingleItem().UserId.ShouldBe(admin.User.Id);
    }
}

/// <summary>The plain test API, where Proton is off as it is by default.</summary>
public class ProtonSwitchedOffTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    [Fact]
    public async Task Asking_while_Proton_is_off_is_a_clear_unavailable_error()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await _steps.Client.PostJsonAsync("/api/proton/messages", new { text = "Hi" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.ProtonUnavailable);
        var usage = await (await _steps.Client.GetAsync("/api/proton/usage", superAdmin.AccessToken)).ReadAsync<ProtonUsageResponse>();
        usage.Available.ShouldBeFalse();
    }
}
