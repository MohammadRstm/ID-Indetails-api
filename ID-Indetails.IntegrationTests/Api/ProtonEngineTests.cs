using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Two cards may wait at once, one Confirm an hour, and tools are deferred past ten: small limits, to prove the
/// engine's caps and tool search.
/// </summary>
public sealed class ProtonEngineApiFactory : ProtonApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Proton:Actions:MaxPendingActions", "2");
        builder.UseSetting("Proton:Actions:MaxConfirmedActionsPerHour", "1");
        builder.UseSetting("Proton:DeferToolsAbove", "10");
    }
}

/// <summary>
/// The rules every action goes through, with a scripted model: several cards wait at once (a changed request replaces
/// only its own card), the caps hold, a card made under other permissions can't be confirmed, and the user can see
/// what Proton proposed and what became of it.
/// </summary>
public class ProtonEngineTests(ProtonEngineApiFactory api) : IClassFixture<ProtonEngineApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<ProtonReplyResponse> AskAsync(AuthResponse who, object body)
    {
        var response = await Client.PostJsonAsync("/api/proton/messages", body, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonReplyResponse>();
    }

    private JsonNode Result(int request) => JsonNode.Parse(api.Anthropic.ToolResults(request).Single()["content"]!.GetValue<string>())!;

    private Task<HttpResponseMessage> ConfirmAsync(AuthResponse who, Guid conversationId, Guid actionId)
        => Client.PostJsonAsync($"/api/proton/actions/{actionId}/confirm", new { conversationId }, who.AccessToken);

    private static object Requirement(long projectId, string text) => new { project_id = projectId, text };

    [Fact]
    public async Task Cards_wait_side_by_side_up_to_the_cap_and_a_changed_request_replaces_only_its_own()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var projects = new ProjectTestSteps(_steps);
        var alpha = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var beta = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("propose_add_requirement", Requirement(alpha.Id, "Check the plans")),
            FakeAnthropic.ToolUse("propose_add_requirement", Requirement(beta.Id, "Order samples")),
            FakeAnthropic.Text("Two cards to confirm."),
            // the same kind of change for the same project replaces its card; a third project is over the cap
            FakeAnthropic.ToolUse("propose_add_requirement", Requirement(alpha.Id, "Check the revised plans")),
            FakeAnthropic.ToolUse("propose_change_project_status", new { project_id = beta.Id, change = "hold" }),
            FakeAnthropic.Text("Done."));

        var first = await AskAsync(superAdmin, new { text = "Add a requirement to each" });
        first.PendingActions.Count.ShouldBe(2);
        first.PendingActions.Select(p => p.Target!.Id).ShouldBe([alpha.Id, beta.Id], ignoreOrder: true);

        var second = await AskAsync(superAdmin, new { conversationId = first.ConversationId, text = "Make Alpha's the revised plans, and put Beta on hold" });
        Result(4)["status"]!.GetValue<string>().ShouldBe("proposed");
        Result(5)["status"]!.GetValue<string>().ShouldBe("refused");
        Result(5)["message"]!.GetValue<string>().ShouldContain("already waiting");
        second.PendingActions.ShouldHaveSingleItem().Target!.Id.ShouldBe(alpha.Id);

        var mine = await (await Client.GetAsync("/api/proton/actions", superAdmin.AccessToken)).ReadAsync<List<ProtonActionSummaryResponse>>();
        mine.Single(a => a.Id == first.PendingActions.Single(p => p.Target!.Id == alpha.Id).Id).Status.ShouldBe(ProtonActionStatus.Superseded);
        mine.Single(a => a.Id == first.PendingActions.Single(p => p.Target!.Id == beta.Id).Id).Status.ShouldBe(ProtonActionStatus.Pending);

        // one Confirm an hour here
        (await ConfirmAsync(superAdmin, first.ConversationId, second.PendingActions[0].Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await ConfirmAsync(superAdmin, first.ConversationId, first.PendingActions.Single(p => p.Target!.Id == beta.Id).Id))
            .ShouldFailWithAsync(422, ErrorCodes.ProtonActionLimit);
    }

    [Fact]
    public async Task Past_the_threshold_only_the_core_tools_are_sent_in_full_and_the_rest_are_found_by_search()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        api.Anthropic.Reply(FakeAnthropic.Text("Hello."), FakeAnthropic.Text("Again."));
        var first = await AskAsync(superAdmin, new { text = "Hi" });
        await AskAsync(superAdmin, new { conversationId = first.ConversationId, text = "And again" });

        var tools = api.Anthropic.Requests[0]["tools"]!.AsArray();
        tools.ShouldContain(t => t!["type"] != null && t["type"]!.GetValue<string>().StartsWith("tool_search_tool_bm25") && t["name"]!.GetValue<string>() == "tool_search_tool_bm25");
        bool Deferred(string name) => tools.Single(t => t!["name"]!.GetValue<string>() == name)!["defer_loading"]?.GetValue<bool>() == true;
        Deferred("find").ShouldBeFalse();
        Deferred("open_page").ShouldBeFalse();
        Deferred("compare_money").ShouldBeFalse();
        Deferred("list_payments").ShouldBeFalse();
        Deferred("get_finance_projects").ShouldBeTrue();
        Deferred("propose_add_requirement").ShouldBeTrue();
        // the same list on every call, so it stays in the cached prefix
        api.Anthropic.Requests[1]["tools"]!.ToJsonString().ShouldBe(tools.ToJsonString());
    }

    [Fact]
    public async Task A_card_made_under_other_permissions_cannot_be_confirmed()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(FakeAnthropic.ToolUse("propose_add_requirement", Requirement(project.Id, "Check the plans")), FakeAnthropic.Text("Confirm it."));
        var reply = await AskAsync(admin, new { text = "Add a requirement" });

        var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "grant", reason = "Handles the money" } } }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());

        await (await ConfirmAsync(admin, reply.ConversationId, reply.PendingActions.Single().Id)).ShouldFailWithAsync(409, ErrorCodes.ProtonActionNotPending);
        var mine = await (await Client.GetAsync("/api/proton/actions", admin.AccessToken)).ReadAsync<List<ProtonActionSummaryResponse>>();
        mine.ShouldHaveSingleItem().Status.ShouldBe(ProtonActionStatus.Superseded);

        // what Proton did, everyone's: for those who manage Proton only
        await (await Client.GetAsync("/api/proton/actions/all", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var all = await (await Client.GetAsync("/api/proton/actions/all", superAdmin.AccessToken)).ReadAsync<List<ProtonActionSummaryResponse>>();
        all.ShouldContain(a => a.Id == reply.PendingActions.Single().Id && a.Who == $"{admin.User.FirstName} {admin.User.LastName}".Trim());
    }
}
