using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Proton.Ui;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Employees have Proton (decision 26): it reads only what they may see, opens their own pages, and proposes changes
/// to their own work only, each confirmed on a card, with the overtime the log form would show.
/// </summary>
public class ProtonEmployeeTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> CallAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Done."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Please do that." }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private async Task ConfirmAsync(AuthResponse who, ProtonReplyResponse reply)
    {
        var response = await Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction!.Id}/confirm", new { reply.ConversationId }, who.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<WorkEntryResponse>> MineAsync(AuthResponse who)
        => (await (await Client.GetAsync("/api/me/work-entries?pageSize=50", who.AccessToken)).ReadAsync<PagedResponse<WorkEntryResponse>>()).Items.ToList();

    [Fact]
    public async Task An_employee_logs_edits_and_deletes_their_own_work_through_cards_and_no_one_elses()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var theirs = await new WorkEntryTestSteps(_steps).LogAsync(colleague.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(30), 60);

        // late evening work: past the day's hours, so the card says how much is overtime
        var yesterday = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).AddDays(-1);
        var (log, _) = await CallAsync(designer, "propose_log_work",
            new { project_id = project.Id, day = yesterday.ToString("yyyy-MM-dd"), start = "20:00", end = "22:30", description = "Night drawings" });
        var card = log.PendingAction.ShouldNotBeNull();
        card.Preview.Changes.Single(c => c.Label == "When").After.ShouldEndWith("20:00–22:30 (2 h 30)");
        card.Preview.Note!.ShouldContain("overtime");
        (await MineAsync(designer)).ShouldBeEmpty();

        await ConfirmAsync(designer, log);
        var entry = (await MineAsync(designer)).ShouldHaveSingleItem();
        entry.Minutes.ShouldBe(150);

        var (_, listed) = await CallAsync(designer, "list_my_work", new { period = "last_week" });
        listed.ToJsonString().ShouldNotContain("\"Id\":" + theirs.Id);

        var (edit, _) = await CallAsync(designer, "propose_update_my_work", new { work_entry_id = entry.Id, end = "22:00" });
        await ConfirmAsync(designer, edit);
        (await MineAsync(designer)).Single().Minutes.ShouldBe(120);

        var (_, notMine) = await CallAsync(designer, "propose_delete_my_work", new { work_entry_id = theirs.Id });
        notMine.ToJsonString().ShouldContain("not_found");

        var (delete, _) = await CallAsync(designer, "propose_delete_my_work", new { work_entry_id = entry.Id });
        await ConfirmAsync(designer, delete);
        (await MineAsync(designer)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_employee_opens_their_own_pages_and_is_offered_no_one_elses_changes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var (opened, _) = await CallAsync(designer, "open_page", new { page = "my_work" });
        opened.UiCommands.ShouldHaveSingleItem().Page.ShouldBe(ProtonPage.MyWork);

        // employees don't see their pay, so there is no pay page to open
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("open_page", new { page = "my_pay" }), FakeAnthropic.Text("Done."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Open my pay." }, designer.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        (await response.ReadAsync<ProtonReplyResponse>()).UiCommands.ShouldBeEmpty();
        api.Anthropic.ToolResults(1).ShouldHaveSingleItem().ToJsonString().ShouldContain("invalid");

        api.Anthropic.ToolsOffered(0).ShouldContain("propose_log_work");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("propose_create_client");
        api.Anthropic.ToolsOffered(0).ShouldNotContain("list_work_entries");
    }
}
