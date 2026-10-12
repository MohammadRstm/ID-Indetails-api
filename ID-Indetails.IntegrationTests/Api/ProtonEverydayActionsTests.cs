using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Teams;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The everyday actions (clients, meetings, teams, the user's own profile, comments), with a scripted model: each is
/// proposed with the endpoint's own checks, changes nothing until confirmed, then goes through the existing service.
/// </summary>
public class ProtonEverydayActionsTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    /// <summary>The model calls <paramref name="tool"/>, then answers; the reply carries the card if one was made.</summary>
    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input)
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = "Please make that change." }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply)
    {
        var response = await Client.PostJsonAsync($"/api/proton/actions/{reply.PendingAction!.Id}/confirm", new { reply.ConversationId }, who.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> GetAsync<T>(AuthResponse who, string path) => await (await Client.GetAsync(path, who.AccessToken)).ReadAsync<T>();

    [Fact]
    public async Task A_client_is_added_with_a_warning_about_its_name_then_edited_archived_and_guarded_against_stale_edits()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var existing = await new ProjectTestSteps(_steps).CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var usedEmail = existing.Contacts[0].Value;
        var name = existing.Name;
        var email = AuthTestSteps.NewEmail("nour");

        // someone who sees both businesses is asked which one
        var (_, unsure) = await ProposeAsync(superAdmin, "propose_create_client", new { name });
        unsure["message"]!.GetValue<string>().ShouldContain("Design or Execution");

        // another client's contact is refused before any card, as the app would refuse it; a shared name is only a warning
        var (_, taken) = await ProposeAsync(superAdmin, "propose_create_client",
            new { name, domain = "design", contacts = new[] { new { kind = "email", value = usedEmail } } });
        taken["message"]!.GetValue<string>().ShouldContain($"already belongs to {existing.DisplayName}");

        var (proposed, result) = await ProposeAsync(superAdmin, "propose_create_client",
            new { name, domain = "design", contacts = new[] { new { kind = "email", value = email } } });
        result["status"]!.GetValue<string>().ShouldBe("proposed");
        var card = proposed.PendingAction!;
        card.Preview.Note.ShouldBe("A client with this name already exists.");
        card.Preview.Area.ShouldBe(DomainCodes.Design);
        card.Preview.OpensAfter.ShouldBe("the new client");
        (await GetAsync<ID_Indetails.api.DTO.Response.Common.PagedResponse<ClientSummaryResponse>>(superAdmin, $"/api/clients?search={name}")).Items.ShouldHaveSingleItem();

        var created = await ConfirmAsync(superAdmin, proposed);
        created.UiCommands.ShouldHaveSingleItem().Id.ShouldBe(created.Refs.Single().Id);
        var clientId = created.Refs.Single().Id;
        var client = await GetAsync<ClientResponse>(superAdmin, $"/api/clients/{clientId}");
        client.Label.ShouldBe("Client");
        client.Contacts.ShouldHaveSingleItem().Value.ShouldBe(email);

        // contacts merge: the email goes, a WhatsApp number joins
        var (edit, _) = await ProposeAsync(superAdmin, "propose_update_client", new
        {
            client_id = clientId,
            organisation = "Nour Holdings",
            add_contacts = new[] { new { kind = "phone", value = "+961 3 123 456", whatsapp = true } },
            remove_contact_ids = new[] { client.Contacts[0].Id },
        });
        edit.PendingAction!.Preview.Changes.Select(c => c.Label).ShouldBe(["Organisation", "Remove Email", "Add WhatsApp"]);
        await ConfirmAsync(superAdmin, edit);
        client = await GetAsync<ClientResponse>(superAdmin, $"/api/clients/{clientId}");
        client.Organisation.ShouldBe("Nour Holdings");
        client.Contacts.ShouldHaveSingleItem().IsWhatsapp.ShouldBeTrue();

        // changed in the app after the card was made: the card is refused rather than overwriting it
        var (stale, _) = await ProposeAsync(superAdmin, "propose_update_client", new { client_id = clientId, note = "From Proton" });
        var changed = await Client.PutJsonAsync($"/api/clients/{clientId}", new
        {
            name = client.Name, organisation = "Changed in the app", label = client.Label, note = (string?)null,
            contacts = client.Contacts.Select(c => new { kind = c.Kind.ToString().ToLowerInvariant(), value = c.Value, label = c.Label, isWhatsapp = c.IsWhatsapp }),
        }, superAdmin.AccessToken);
        changed.IsSuccessStatusCode.ShouldBeTrue(await changed.Content.ReadAsStringAsync());
        await (await Client.PostJsonAsync($"/api/proton/actions/{stale.PendingAction!.Id}/confirm", new { stale.ConversationId }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProtonActionStale);

        var (archive, _) = await ProposeAsync(superAdmin, "propose_set_client_archived", new { client_id = clientId, archived = true });
        await ConfirmAsync(superAdmin, archive);
        (await GetAsync<ClientResponse>(superAdmin, $"/api/clients/{clientId}")).IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task A_meeting_is_set_and_moved_in_company_time()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var day = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).AddDays(3);
        DateTime Beirut(int hour, int minute)
            => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut"));

        var (set, _) = await ProposeAsync(superAdmin, "propose_schedule_meeting",
            new { project_id = project.Id, day = day.ToString("yyyy-MM-dd"), start = "14:30", minutes = 45, topic = "Site review" });
        set.PendingAction!.Preview.Changes[0].After.ShouldEndWith("14:30–15:15");
        await ConfirmAsync(superAdmin, set);
        var meeting = (await GetAsync<List<MeetingResponse>>(superAdmin, $"/api/projects/{project.Id}/meetings")).ShouldHaveSingleItem();
        meeting.StartsAt.ShouldBe(Beirut(14, 30));
        meeting.Minutes.ShouldBe(45);

        var (move, _) = await ProposeAsync(superAdmin, "propose_update_meeting", new { project_id = project.Id, meeting_id = meeting.Id, start = "16:00" });
        move.PendingAction!.Preview.Changes.ShouldHaveSingleItem().Label.ShouldBe("When");
        var moved = await ConfirmAsync(superAdmin, move);
        moved.Message.ShouldContain("16:00–16:45");
        (await GetAsync<List<MeetingResponse>>(superAdmin, $"/api/projects/{project.Id}/meetings")).Single().StartsAt.ShouldBe(Beirut(16, 0));

        var (_, invalid) = await ProposeAsync(superAdmin, "propose_schedule_meeting", new { project_id = project.Id, day = day.ToString("yyyy-MM-dd"), start = "2pm" });
        invalid["message"]!.GetValue<string>().ShouldContain("HH:mm");
    }

    [Fact]
    public async Task Teams_are_added_and_renamed_and_a_profile_changes_only_its_owner()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = "Lighting " + Guid.NewGuid().ToString("N")[..6];
        var (add, _) = await ProposeAsync(superAdmin, "propose_create_team", new { name, domain = "design" });
        await ConfirmAsync(superAdmin, add);
        var team = (await GetAsync<List<TeamResponse>>(superAdmin, "/api/teams")).Single(t => t.Name == name);

        var (_, duplicate) = await ProposeAsync(superAdmin, "propose_create_team", new { name, domain = "design" });
        duplicate["message"]!.GetValue<string>().ShouldContain("already a team");

        var (rename, _) = await ProposeAsync(superAdmin, "propose_rename_team", new { team_id = team.Id, name = name + " & Power" });
        await ConfirmAsync(superAdmin, rename);
        (await GetAsync<List<TeamResponse>>(superAdmin, "/api/teams")).Single(t => t.Id == team.Id).Name.ShouldBe(name + " & Power");

        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (profile, _) = await ProposeAsync(admin, "propose_update_my_profile", new { phone = "+961 70 111 222" });
        profile.PendingAction!.Preview.Changes.ShouldHaveSingleItem().Label.ShouldBe("Phone");
        await ConfirmAsync(admin, profile);
        (await GetAsync<CurrentUserResponse>(admin, "/api/auth/me")).Phone.ShouldBe("+961 70 111 222");
        (await GetAsync<CurrentUserResponse>(superAdmin, "/api/auth/me")).Phone.ShouldNotBe("+961 70 111 222");
    }

    [Fact]
    public async Task Strong_families_stay_off_until_switched_on()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        foreach (var (tool, input) in new (string, object)[]
        {
            ("propose_project_lifecycle", new { project_id = project.Id, change = "complete" }),
            ("propose_deactivate_employee", new { user_id = superAdmin.User.Id }),
            ("propose_domain_settings", new { domain = "design", hold_logging = true }),
            ("propose_send_message", new { person_id = superAdmin.User.Id, body = "Hi" }),
            ("propose_record_payment", new { project_id = project.Id, amount = 100m }),
            ("propose_delete", new { kind = "client", id = project.Client!.Id }),
            ("propose_hour_excuse", new { user_id = superAdmin.User.Id, month = "2026-10", minutes = 30 }),
        })
        {
            var (reply, result) = await ProposeAsync(superAdmin, tool, input);
            reply.PendingAction.ShouldBeNull(tool);
            result["message"]!.GetValue<string>().ShouldContain("switched on", customMessage: tool);
        }
    }

    [Fact]
    public async Task A_comment_reaches_the_entry_and_needs_the_comment_permission()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var entry = await new WorkEntryTestSteps(_steps).LogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60);

        var (comment, _) = await ProposeAsync(superAdmin, "propose_comment_work_entry", new { work_entry_id = entry.Id, body = "Great progress" });
        comment.PendingAction!.Preview.Note.ShouldBe($"{entry.Person.Name} is notified.");
        await ConfirmAsync(superAdmin, comment);
        (await GetAsync<List<WorkEntryCommentResponse>>(superAdmin, $"/api/work-entries/{entry.Id}/comments")).ShouldHaveSingleItem().Body.ShouldBe("Great progress");

        var revoked = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WorkEntryComment, action = "revoke", reason = "test" } } }, superAdmin.AccessToken);
        revoked.IsSuccessStatusCode.ShouldBeTrue(await revoked.Content.ReadAsStringAsync());
        var (refused, result) = await ProposeAsync(admin, "propose_comment_work_entry", new { work_entry_id = entry.Id, body = "Mine" });
        refused.PendingAction.ShouldBeNull();
        result.ToJsonString().ShouldContain("forbidden");
    }
}
