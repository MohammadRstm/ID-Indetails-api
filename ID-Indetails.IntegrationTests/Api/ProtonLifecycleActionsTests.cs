using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Completing a client project through Proton, with a scripted model: the card shows what the client still owes, and
/// excusing or writing it off is the Money family's change, confirmed with the amount typed and refused once the balance
/// moved. Every test follows its own project, since the classes share one database.
/// </summary>
public class ProtonLifecycleActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, object input, string said = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("propose_project_lifecycle", input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = said }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private async Task<JsonNode> ReadAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here it is."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "How does its money look?" }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
    }

    private Task<HttpResponseMessage> TryConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed)
        => Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction!.Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string typed)
    {
        var response = await TryConfirmAsync(who, reply, typed);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<ProjectResponse> ProjectAsync(AuthResponse who, long projectId)
        => await (await Client.GetAsync($"/api/projects/{projectId}", who.AccessToken)).ReadAsync<ProjectResponse>();

    private async Task PayAsync(long projectId, decimal amount, string token)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/payments", new { amount, currency = "USD" }, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A Design client project of 1,000 before 11% TVA (1,110 for the client), of which the client paid 555.</summary>
    private async Task<ProjectResponse> OwingProjectAsync(AuthResponse superAdmin)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 1_000m,
            tvaRate = 0.11m,
            departmentIds = new[] { await _steps.DesignDepartmentAsync() },
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var project = await created.ReadAsync<ProjectResponse>();
        await PayAsync(project.Id, 555m, superAdmin.AccessToken);
        return project;
    }

    [Fact]
    public async Task Completing_shows_what_the_client_still_owes_and_writes_it_off_once_the_amount_is_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await OwingProjectAsync(superAdmin);

        // completing alone leaves the balance owed, and the card says so before the revenue locks
        var (plain, _) = await ProposeAsync(superAdmin, new { project_id = project.Id, change = "complete" });
        plain.PendingAction!.Preview.Phrase.ShouldBe(project.Title);
        plain.PendingAction.Preview.Note!.ShouldContain("The client still owes USD 555 (TVA included); it stays owed");

        var (settle, _) = await ProposeAsync(superAdmin, new { project_id = project.Id, change = "complete", settle_owed = "written_off" },
            "Complete it and write off what they still owe.");
        var card = settle.PendingAction!;
        card.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        card.Preview.Phrase.ShouldBe("555");
        card.Preview.Changes.ShouldContain(c => c.Label == "Owed by the client (TVA included)" && c.Before == "USD 555");
        card.Preview.Changes.ShouldContain(c => c.Label == "Price with TVA" && c.Before == "USD 1,110" && c.After == "USD 555");

        // the project's name doesn't confirm a settlement: the amount does
        await (await TryConfirmAsync(superAdmin, settle, project.Title)).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        (await ProjectAsync(superAdmin, project.Id)).Status.ShouldBe(ProjectStatus.Active);

        var outcome = await ConfirmAsync(superAdmin, settle, "555");
        outcome.Message.ShouldContain("written off");
        var done = await ProjectAsync(superAdmin, project.Id);
        done.Status.ShouldBe(ProjectStatus.Completed);
        done.Money!.ShouldSatisfyAllConditions(
            m => m.Billing.Owed.ShouldBe(0m),
            m => m.SettledOwedUsd.ShouldBe(555m),
            m => m.SettledOwedAs.ShouldBe(OwedSettlement.WrittenOff));

        // read back, the lower price comes with what was settled and how
        var money = (await ReadAsync(superAdmin, "get_project_financials", new { project_id = project.Id }))["data"]!;
        money["settled_owed"]!.GetValue<decimal>().ShouldBe(555m);
        money["settled_owed_as"]!.GetValue<string>().ShouldBe("written_off");
    }

    [Fact]
    public async Task A_settlement_is_refused_once_the_balance_moved_or_when_nothing_is_owed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await OwingProjectAsync(superAdmin);

        var (settle, _) = await ProposeAsync(superAdmin, new { project_id = project.Id, change = "complete", settle_owed = "excused" },
            "Complete it and excuse what they still owe.");
        settle.PendingAction!.Preview.Phrase.ShouldBe("555");

        // a payment after the card: the 555 typed would no longer be what is excused
        await PayAsync(project.Id, 100m, superAdmin.AccessToken);
        await (await TryConfirmAsync(superAdmin, settle, "555")).ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);
        var still = await ProjectAsync(superAdmin, project.Id);
        still.Status.ShouldBe(ProjectStatus.Active);
        still.Money!.SettledOwedAs.ShouldBeNull();

        await PayAsync(project.Id, 455m, superAdmin.AccessToken);
        var (paid, result) = await ProposeAsync(superAdmin, new { project_id = project.Id, change = "complete", settle_owed = "excused" },
            "Complete it and excuse what they still owe.");
        paid.PendingAction.ShouldBeNull();
        result["message"]!.GetValue<string>().ShouldContain("owes nothing");
    }

    [Fact]
    public async Task Only_completing_can_settle_and_never_both_ways_at_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await OwingProjectAsync(superAdmin);

        var (archive, archiving) = await ProposeAsync(superAdmin, new { project_id = project.Id, change = "archive", settle_owed = "excused" });
        archive.PendingAction.ShouldBeNull();
        archiving["message"]!.GetValue<string>().ShouldContain("Only completing");

        var (both, bothWays) = await ProposeAsync(superAdmin,
            new { project_id = project.Id, change = "complete", settle_owed = "excused", keep_paid_ahead = true });
        both.PendingAction.ShouldBeNull();
        bothWays["message"]!.GetValue<string>().ShouldContain("Either settle");
    }
}
