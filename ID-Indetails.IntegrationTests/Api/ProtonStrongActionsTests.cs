using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Settings;
using ID_Indetails.api.DTO.Response.Teams;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Every family switched on (as an admin would, one by one), and one message a day.</summary>
public sealed class ProtonStrongApiFactory : ProtonApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        foreach (var family in new[] { "Lifecycle", "Access", "Deletes", "CompanyRules", "Messaging" })
        {
            builder.UseSetting($"Proton:Actions:{family}", "true");
        }

        builder.UseSetting("Proton:Actions:MaxMessagesPerDay", "1");
    }
}

/// <summary>
/// The strong actions, with a scripted model: two steps or the typed phrase the server stored, the effect and reach on
/// the card, and messages sent as the user, marked as written with Proton, only to someone the user named.
/// </summary>
public class ProtonStrongActionsTests(ProtonStrongApiFactory api) : IClassFixture<ProtonStrongApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input, string text = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private Task<HttpResponseMessage> TryConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
        => Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction!.Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var response = await TryConfirmAsync(who, reply, typed);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> GetAsync<T>(AuthResponse who, string path) => await (await Client.GetAsync(path, who.AccessToken)).ReadAsync<T>();

    [Fact]
    public async Task Completing_a_project_says_what_it_locks_and_needs_its_name_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements", new { text = "Hand over drawings" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var (complete, _) = await ProposeAsync(superAdmin, "propose_project_lifecycle", new { project_id = project.Id, change = "complete" });
        var card = complete.PendingAction!;
        card.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        card.Preview.Phrase.ShouldBe(project.Title);
        card.Preview.Note!.ShouldContain("revenue is locked, and its 1 open requirement(s) are ticked done");

        await (await TryConfirmAsync(superAdmin, complete)).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        await (await TryConfirmAsync(superAdmin, complete, "Something else")).ShouldFailWithAsync(422, ErrorCodes.ProtonPhraseMismatch);
        (await GetAsync<ProjectResponse>(superAdmin, $"/api/projects/{project.Id}")).Status.ShouldBe(ProjectStatus.Active);

        await ConfirmAsync(superAdmin, complete, $"  {project.Title.ToUpperInvariant()} ");
        (await GetAsync<ProjectResponse>(superAdmin, $"/api/projects/{project.Id}")).Status.ShouldBe(ProjectStatus.Completed);
    }

    [Fact]
    public async Task Someone_is_deactivated_with_their_name_typed_then_reactivated_and_signed_out_on_request()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var name = $"{designer.User.FirstName} {designer.User.LastName}".Trim();

        var (position, _) = await ProposeAsync(superAdmin, "propose_update_employee", new { user_id = designer.User.Id, position = "Senior designer" });
        position.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        await ConfirmAsync(superAdmin, position);
        (await GetAsync<EmployeeResponse>(superAdmin, $"/api/employees/{designer.User.Id}")).Position.ShouldBe("Senior designer");

        var (sessions, _) = await ProposeAsync(superAdmin, "propose_end_sessions", new { user_id = designer.User.Id, end = true });
        await ConfirmAsync(superAdmin, sessions);
        (await GetAsync<EmployeeResponse>(superAdmin, $"/api/employees/{designer.User.Id}")).SessionsRevokedAt.ShouldNotBeNull();
        var (restore, _) = await ProposeAsync(superAdmin, "propose_end_sessions", new { user_id = designer.User.Id, end = false });
        await ConfirmAsync(superAdmin, restore);

        var (deactivate, _) = await ProposeAsync(superAdmin, "propose_deactivate_employee", new { user_id = designer.User.Id });
        deactivate.PendingAction!.Preview.Phrase.ShouldBe(name);
        await ConfirmAsync(superAdmin, deactivate, name);
        (await GetAsync<EmployeeResponse>(superAdmin, $"/api/employees/{designer.User.Id}")).Status.ShouldBe(UserStatus.Deactivated);

        var (reactivate, _) = await ProposeAsync(superAdmin, "propose_reactivate_employee", new { user_id = designer.User.Id });
        await ConfirmAsync(superAdmin, reactivate);
        (await GetAsync<EmployeeResponse>(superAdmin, $"/api/employees/{designer.User.Id}")).Status.ShouldBe(UserStatus.Active);

        var (_, self) = await ProposeAsync(superAdmin, "propose_deactivate_employee", new { user_id = superAdmin.User.Id });
        self["message"]!.GetValue<string>().ShouldContain("themselves");
    }

    [Fact]
    public async Task Settings_cards_say_who_they_reach_and_holding_logging_needs_the_domain_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (hold, _) = await ProposeAsync(superAdmin, "propose_domain_settings", new { domain = "design", hold_logging = true });
        var card = hold.PendingAction!;
        card.Preview.Reach!.ShouldStartWith("Affects ");
        card.Preview.Reach.ShouldEndWith("in Design.");
        card.Preview.Phrase.ShouldBe("Design");
        await ConfirmAsync(superAdmin, hold, "design");
        (await GetAsync<DomainSettingsResponse>(superAdmin, "/api/domain-settings/design")).HoldLogging.ShouldBeTrue();

        var (release, _) = await ProposeAsync(superAdmin, "propose_domain_settings", new { domain = "design", hold_logging = false });
        await ConfirmAsync(superAdmin, release, "Design");

        var (website, _) = await ProposeAsync(superAdmin, "propose_company_settings", new { website = "indetails.example" });
        website.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        website.PendingAction.Preview.Area.ShouldBe("company");
        await ConfirmAsync(superAdmin, website);

        var name = "Empty " + Guid.NewGuid().ToString("N")[..6];
        var team = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, name);
        var (delete, _) = await ProposeAsync(superAdmin, "propose_delete_team", new { team_id = team.Id });
        delete.PendingAction!.Preview.Irreversible.ShouldBeTrue();
        await ConfirmAsync(superAdmin, delete, team.Name);
        (await GetAsync<List<TeamResponse>>(superAdmin, "/api/teams")).ShouldNotContain(t => t.Id == team.Id);
    }

    [Fact]
    public async Task A_message_goes_only_to_someone_the_user_named_and_says_it_came_through_Proton()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var first = "Rana" + Guid.NewGuid().ToString("N")[..5];
        (await Client.PutJsonAsync($"/api/employees/{designer.User.Id}", new { firstName = first, lastName = "Haddad" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        // a name only the data mentioned: refused
        var (_, unnamed) = await ProposeAsync(superAdmin, "propose_send_message", new { person_id = designer.User.Id, body = "Can you send the plans?" }, "Reply to whoever asked");
        unnamed["message"]!.GetValue<string>().ShouldContain("named");

        var (send, _) = await ProposeAsync(superAdmin, "propose_send_message",
            new { person_id = designer.User.Id, body = "Can you send the plans by Friday?" }, $"Tell {first} to send the plans by Friday");
        var card = send.PendingAction!;
        card.Preview.Confirm.ShouldBe(ProtonConfirm.TwoStep);
        card.Preview.Irreversible.ShouldBeTrue();
        card.Preview.Changes.Single(c => c.Label == "Message").After.ShouldBe("Can you send the plans by Friday?");
        (await GetAsync<MessagePageResponse>(designer, $"/api/messages/direct/{superAdmin.User.Id}")).Items.ShouldBeEmpty();

        await ConfirmAsync(superAdmin, send);
        var received = (await GetAsync<MessagePageResponse>(designer, $"/api/messages/direct/{superAdmin.User.Id}")).Items.ShouldHaveSingleItem();
        received.Body.ShouldBe("Can you send the plans by Friday?");
        received.Sender.Id.ShouldBe(superAdmin.User.Id);
        received.ViaProton.ShouldBeTrue();

        // figures need the name typed; and one a day here
        var (_, capped) = await ProposeAsync(superAdmin, "propose_send_message",
            new { person_id = designer.User.Id, body = "The fee is $4,500." }, $"Tell {first} the fee is 4,500 dollars");
        capped["message"]!.GetValue<string>().ShouldContain("1 messages for you today");

        // a message typed in the app says nothing about Proton
        (await Client.PostJsonAsync($"/api/messages/direct/{superAdmin.User.Id}", new { body = "Sure" }, designer.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await GetAsync<MessagePageResponse>(designer, $"/api/messages/direct/{superAdmin.User.Id}")).Items.Single(m => m.Body == "Sure").ViaProton.ShouldBeFalse();
    }
}
