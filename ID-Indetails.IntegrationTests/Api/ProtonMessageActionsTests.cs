using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.BoardLayouts;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Proton.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The inbox, announcement, bug report, general meeting, work-entry and dashboard-order actions, with a scripted model:
/// each is proposed as a card, goes through its existing service only on Confirm, and has a refusal.
/// </summary>
public class ProtonMessageActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input, string said = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = said }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var card = reply.PendingAction.ShouldNotBeNull();
        var response = await Client.PostJsonAsync($"/api/proton/actions/{card.Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<MessagesBadgeResponse> BadgeAsync(AuthResponse who)
        => OkAsync<MessagesBadgeResponse>(Client.GetAsync("/api/messages/badge", who.AccessToken));

    private async Task<List<NotificationResponse>> UnreadNotificationsAsync(AuthResponse who)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?unreadOnly=true", who.AccessToken))).Items.ToList();

    [Fact]
    public async Task A_chat_is_marked_read_only_when_it_has_unread_messages()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/messages/direct/{superAdmin.User.Id}", new { body = "Morning" }, designer.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var (card, _) = await ProposeAsync(superAdmin, "propose_mark_chat_read", new { person_id = designer.User.Id });
        card.PendingAction!.Preview.Changes.Single().Before.ShouldBe("1");
        (await BadgeAsync(superAdmin)).Unread.ShouldBeGreaterThan(0);

        await ConfirmAsync(superAdmin, card);
        var chat = (await OkAsync<List<ConversationResponse>>(Client.GetAsync("/api/messages/conversations", superAdmin.AccessToken)))
            .Single(c => c.Person?.Id == designer.User.Id);
        chat.Unread.ShouldBe(0);

        var (_, nothing) = await ProposeAsync(superAdmin, "propose_mark_chat_read", new { person_id = designer.User.Id });
        nothing["message"]!.GetValue<string>().ShouldContain("Nothing is unread");
    }

    [Fact]
    public async Task Notifications_are_marked_read_one_by_one_or_all_and_only_the_users_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var unread = await UnreadNotificationsAsync(designer);
        unread.Count.ShouldBeGreaterThanOrEqualTo(2);
        var (one, _) = await ProposeAsync(designer, "propose_mark_notifications_read", new { notification_ids = new[] { unread[0].Id } });
        await ConfirmAsync(designer, one);
        var after = await UnreadNotificationsAsync(designer);
        after.ShouldNotContain(n => n.Id == unread[0].Id);
        after.Count.ShouldBe(unread.Count - 1);

        // someone else's notification isn't one of the user's
        var theirs = (await UnreadNotificationsAsync(colleague)).First();
        var (_, notMine) = await ProposeAsync(designer, "propose_mark_notifications_read", new { notification_ids = new[] { theirs.Id } });
        notMine["message"]!.GetValue<string>().ShouldContain("isn't one of the user's unread");
        var (_, both) = await ProposeAsync(designer, "propose_mark_notifications_read", new { all = true, notification_ids = new[] { unread[1].Id } });
        both["message"]!.GetValue<string>().ShouldContain("Give notification_ids, or all");

        var (all, _) = await ProposeAsync(designer, "propose_mark_notifications_read", new { all = true });
        await ConfirmAsync(designer, all);
        (await UnreadNotificationsAsync(designer)).ShouldBeEmpty();
        (await UnreadNotificationsAsync(colleague)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task An_announcement_is_typed_to_post_and_is_the_users_words_byte_for_byte()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var body = "The office is closed on Friday " + Guid.NewGuid().ToString("N")[..6];

        var (card, _) = await ProposeAsync(superAdmin, "propose_send_announcement", new { body }, $"Announce: {body}");
        card.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        card.PendingAction.Preview.Phrase.ShouldBe("announce");
        card.PendingAction.Preview.Changes.Single(c => c.Label == "Message").After.ShouldBe(body);

        // not without the phrase
        var early = await Client.PostJsonAsync($"/api/proton/actions/{card.PendingAction.Id}/confirm", new { card.ConversationId }, superAdmin.AccessToken);
        early.IsSuccessStatusCode.ShouldBeFalse();
        (await OkAsync<MessagePageResponse>(Client.GetAsync("/api/messages/announcements", designer.AccessToken))).Items.ShouldNotContain(m => m.Body == body);

        await ConfirmAsync(superAdmin, card, "announce");
        var posted = (await OkAsync<MessagePageResponse>(Client.GetAsync("/api/messages/announcements", designer.AccessToken))).Items.Single(m => m.Body == body);
        (posted.Sender.Id, posted.ViaProton).ShouldBe((superAdmin.User.Id, true));

        // an employee can't announce, whatever Proton is asked
        var (refused, _) = await ProposeAsync(designer, "propose_send_announcement", new { body = "Hello all" });
        refused.PendingAction.ShouldBeNull();
    }

    [Fact]
    public async Task A_bug_report_is_emailed_to_the_developers_only_on_confirm()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var support = api.Services.GetRequiredService<IOptions<BugReportOptions>>().Value.ToAddress;
        var title = "Overview went blank " + Guid.NewGuid().ToString("N")[..6];
        var before = api.Emails.CountTo(support);

        var (card, _) = await ProposeAsync(superAdmin, "propose_report_bug",
            new { title, description = "It said something went wrong.", page = "/design/overview" }, "Report that the overview went blank");
        card.PendingAction!.Preview.Irreversible.ShouldBeTrue();
        card.PendingAction.Preview.Changes.Single(c => c.Label == "Title").After.ShouldBe(title);
        api.Emails.CountTo(support).ShouldBe(before);

        await ConfirmAsync(superAdmin, card);
        api.Emails.CountTo(support).ShouldBe(before + 1);
        api.Emails.LastTo(support).Subject.ShouldEndWith(title);
        api.Emails.LastTo(support).TextBody.ShouldContain("Proton");
    }

    [Fact]
    public async Task A_general_meeting_is_set_with_the_people_named_and_needs_someone_besides_the_user()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, attendee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var day = DateTime.UtcNow.Date.AddDays(4).ToString("yyyy-MM-dd");
        var topic = "Studio sync " + Guid.NewGuid().ToString("N")[..6];

        var (_, alone) = await ProposeAsync(superAdmin, "propose_create_general_meeting", new { day, start = "10:00", topic, attendee_ids = new[] { superAdmin.User.Id } });
        alone["message"]!.GetValue<string>().ShouldContain("Choose who the meeting is with");

        var (card, _) = await ProposeAsync(superAdmin, "propose_create_general_meeting",
            new { day, start = "10:00", minutes = 45, topic, attendee_ids = new[] { attendee.User.Id } });
        card.PendingAction!.Preview.Note!.ShouldContain("no invitation is emailed");
        (await OkAsync<List<MeetingResponse>>(Client.GetAsync("/api/meetings", superAdmin.AccessToken))).ShouldNotContain(m => m.Topic == topic);

        await ConfirmAsync(superAdmin, card);
        var meeting = (await OkAsync<List<MeetingResponse>>(Client.GetAsync("/api/meetings", superAdmin.AccessToken))).Single(m => m.Topic == topic);
        (meeting.EndsAt - meeting.StartsAt).ShouldBe(TimeSpan.FromMinutes(45));
        meeting.Attendees.ShouldHaveSingleItem().Id.ShouldBe(attendee.User.Id);
    }

    [Fact]
    public async Task A_deleted_entry_is_restored_by_its_owner_and_an_admin_changes_an_entrys_scope()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var work = new WorkEntryTestSteps(_steps);
        var entry = await work.LogAsync(designer.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(5, 40), 40);
        var theirs = await work.LogAsync(colleague.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(5, 40), 40);
        ((int)(await Client.DeleteAsync($"/api/work-entries/{entry.Id}", designer.AccessToken)).StatusCode).ShouldBe(204);
        ((int)(await Client.DeleteAsync($"/api/work-entries/{theirs.Id}", colleague.AccessToken)).StatusCode).ShouldBe(204);

        // only the user's own deleted entry
        var (_, notMine) = await ProposeAsync(designer, "propose_restore_my_work", new { work_entry_id = theirs.Id });
        notMine.ToJsonString().ShouldContain("not_found");
        var (restore, _) = await ProposeAsync(designer, "propose_restore_my_work", new { work_entry_id = entry.Id });
        await ConfirmAsync(designer, restore);
        (await OkAsync<WorkEntryDetailResponse>(Client.GetAsync($"/api/work-entries/{entry.Id}", designer.AccessToken))).Entry.Id.ShouldBe(entry.Id);

        var (_, same) = await ProposeAsync(superAdmin, "propose_set_work_entry_scope", new { work_entry_id = entry.Id, outside_scope = false });
        same["message"]!.GetValue<string>().ShouldContain("already inside the scope");
        var (scope, _) = await ProposeAsync(superAdmin, "propose_set_work_entry_scope", new { work_entry_id = entry.Id, outside_scope = true });
        await ConfirmAsync(superAdmin, scope);
        (await OkAsync<WorkEntryDetailResponse>(Client.GetAsync($"/api/work-entries/{entry.Id}", superAdmin.AccessToken))).Entry.OutsideScope.ShouldBeTrue();
    }

    [Fact]
    public async Task A_dashboard_is_rearranged_and_put_back_for_the_user_alone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var layout = $"/api/me/board-layouts/{BoardKeys.ExecutionOverview}";

        var (_, incomplete) = await ProposeAsync(admin, "propose_arrange_board", new { board = "execution_overview", order = new[] { "status" } });
        incomplete["message"]!.GetValue<string>().ShouldContain("every card of the board once");

        var (arrange, _) = await ProposeAsync(admin, "propose_arrange_board", new { board = "execution_overview", order = new[] { "status", "figures" } });
        await ConfirmAsync(admin, arrange);
        (await OkAsync<BoardLayoutResponse>(Client.GetAsync(layout, admin.AccessToken))).Order.ShouldBe(["status", "figures"]);
        (await OkAsync<BoardLayoutResponse>(Client.GetAsync(layout, superAdmin.AccessToken))).Order.ShouldBeNull();

        var (reset, _) = await ProposeAsync(admin, "propose_arrange_board", new { board = "execution_overview", reset = true });
        await ConfirmAsync(admin, reset);
        (await OkAsync<BoardLayoutResponse>(Client.GetAsync(layout, admin.AccessToken))).Order.ShouldBeNull();
    }
}
