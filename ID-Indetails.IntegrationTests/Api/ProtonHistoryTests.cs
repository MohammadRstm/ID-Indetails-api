using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Saved chats with a scripted model: each answered question is kept as the user saw it, only its owner reaches it, it
/// carries on after the server let it go, it isn't shown under access the owner lost, and deleting it removes its words.
/// </summary>
public class ProtonHistoryTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private Task<HttpResponseMessage> AskAsync(AuthResponse who, object body)
        => Client.PostJsonAsync("/api/proton/messages", body, who.AccessToken);

    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<ProtonConversationResponse> ChatAsync(AuthResponse who, Guid id)
        => OkAsync<ProtonConversationResponse>(Client.GetAsync($"/api/proton/conversations/{id}", who.AccessToken));

    /// <summary>The server lets a chat go after a while idle, or on restart.</summary>
    private void Forget(AuthResponse who, Guid id) => api.Services.GetRequiredService<ProtonConversationStore>().Remove(who.User.Id, id);

    private async Task<AuthResponse> SuperAdminAsync()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        return superAdmin;
    }

    [Fact]
    public async Task Each_answered_question_is_saved_as_the_user_saw_it()
    {
        var superAdmin = await SuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("get_project", new { project_id = project.Id }, id: "toolu_0"),
            FakeAnthropic.Blocks("tool_use",
                new { type = "text", text = $"{project.Title} is active." },
                new { type = "tool_use", id = "toolu_1", name = "suggest_replies", input = new { replies = new[] { "Open it" } } }),
            FakeAnthropic.Text("Want me to open it?"),
            FakeAnthropic.ToolUse("open_record", new { type = "project", id = project.Id }, id: "toolu_2"),
            FakeAnthropic.Text("Opening it."));

        var first = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { text = $"   How is {project.Title}\n doing these days and is anything late or worth a look this week?" }));
        await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { conversationId = first.ConversationId, suggestion = 0 }));

        var list = await OkAsync<PagedResponse<ProtonConversationSummaryResponse>>(Client.GetAsync("/api/proton/conversations", superAdmin.AccessToken));
        var summary = list.Items.Single(c => c.Id == first.ConversationId);
        summary.MessageCount.ShouldBe(4);
        // the first question on one line, cut at a word
        summary.Title.ShouldStartWith($"How is {project.Title} doing these days");
        summary.Title.Length.ShouldBeLessThanOrEqualTo(61);
        summary.Title.ShouldEndWith("…");

        var chat = await ChatAsync(superAdmin, first.ConversationId);
        chat.CanContinue.ShouldBeTrue();
        chat.Hidden.ShouldBeNull();
        chat.Messages.Select(m => (m.Seq, m.Role)).ShouldBe([(1, ProtonMessageRole.User), (2, ProtonMessageRole.Proton), (3, ProtonMessageRole.User), (4, ProtonMessageRole.Proton)]);
        chat.Messages[0].Origin.ShouldBe(ProtonMessageOrigin.Typed);
        chat.Messages[1].Text.ShouldBe($"{project.Title} is active.\n\nWant me to open it?");
        chat.Messages[1].Refs.ShouldContain(r => r.Id == project.Id);
        // a tapped suggestion is saved as the words on it, not the text the app wrote for the model
        chat.Messages[2].Origin.ShouldBe(ProtonMessageOrigin.Suggestion);
        chat.Messages[2].Text.ShouldBe("Open it");
        chat.Messages[3].Text.ShouldBe("Opening it.");
    }

    [Fact]
    public async Task Only_the_owner_reaches_a_saved_chat()
    {
        var superAdmin = await SuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        api.Anthropic.Reply(FakeAnthropic.Text("Hello."));
        var mine = await OkAsync<ProtonReplyResponse>(AskAsync(admin, new { text = "Hi" }));

        await (await Client.GetAsync($"/api/proton/conversations/{mine.ConversationId}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PatchJsonAsync($"/api/proton/conversations/{mine.ConversationId}", new { title = "Mine now" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await Client.DeleteAsync($"/api/proton/conversations/{mine.ConversationId}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OkAsync<PagedResponse<ProtonConversationSummaryResponse>>(Client.GetAsync("/api/proton/conversations", superAdmin.AccessToken)))
            .Items.ShouldNotContain(c => c.Id == mine.ConversationId);

        // still there for its owner, and renamed only by them
        (await ChatAsync(admin, mine.ConversationId)).Messages.Count.ShouldBe(2);
        var renamed = await OkAsync<ProtonConversationSummaryResponse>(
            Client.PatchJsonAsync($"/api/proton/conversations/{mine.ConversationId}", new { title = "  Morning   check  " }, admin.AccessToken));
        renamed.Title.ShouldBe("Morning check");
        await (await Client.PatchJsonAsync($"/api/proton/conversations/{mine.ConversationId}", new { title = "" }, admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_saved_chat_carries_on_after_the_server_let_it_go()
    {
        var superAdmin = await SuperAdminAsync();
        api.Anthropic.Reply(FakeAnthropic.Text("Tower Seven is on track."), FakeAnthropic.Text("Still on track."));
        var first = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { text = "How is Tower Seven?" }));
        Forget(superAdmin, first.ConversationId);

        var next = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { conversationId = first.ConversationId, text = "And now?" }));

        next.ConversationId.ShouldBe(first.ConversationId);
        // the model starts from the saved exchange, as after a long chat's rollover
        var sent = api.Anthropic.Requests[1]["messages"]!.ToJsonString();
        sent.ShouldContain("Earlier in this chat");
        sent.ShouldContain("Tower Seven is on track.");
        (await ChatAsync(superAdmin, first.ConversationId)).Messages.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_chat_from_before_an_access_change_is_not_carried_on_and_hidden_once_access_is_lost()
    {
        var superAdmin = await SuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        api.Anthropic.Reply(FakeAnthropic.Text("Hello."), FakeAnthropic.Text("Hello again."));
        var chat = await OkAsync<ProtonReplyResponse>(AskAsync(admin, new { text = "Hi" }));

        // more access: still readable, but answers given under the old access aren't carried on
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "grant", reason = "Test" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        Forget(admin, chat.ConversationId);
        await (await AskAsync(admin, new { conversationId = chat.ConversationId, text = "Still there?" }))
            .ShouldFailWithAsync(404, ErrorCodes.ProtonConversationEnded);
        var readable = await ChatAsync(admin, chat.ConversationId);
        readable.CanContinue.ShouldBeFalse();
        readable.Messages.Count.ShouldBe(2);

        // less access than when it was held: its content isn't shown at all
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ReportView, action = "revoke", reason = "Test" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var hidden = await ChatAsync(admin, chat.ConversationId);
        hidden.Hidden.ShouldBe(ProtonHistoryService.HiddenText);
        hidden.Messages.ShouldBeEmpty();
        hidden.CanContinue.ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_chat_removes_its_words_and_keeps_its_cost()
    {
        var superAdmin = await SuperAdminAsync();
        api.Anthropic.Reply(FakeAnthropic.Text("The Cedar account is late."));
        var chat = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { text = "What about the Cedar account?" }));

        (await Client.DeleteAsync($"/api/proton/conversations/{chat.ConversationId}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await Client.GetAsync($"/api/proton/conversations/{chat.ConversationId}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await AskAsync(superAdmin, new { conversationId = chat.ConversationId, text = "And?" })).ShouldFailWithAsync(404, ErrorCodes.ProtonConversationEnded);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        var usage = await db.ProtonUsage.AsNoTracking().SingleAsync(u => u.ConversationId == chat.ConversationId);
        usage.Question.ShouldBe("[deleted]");
        usage.Answer.ShouldBeNull();
        usage.CostUsd.ShouldBeGreaterThan(0m);
        (await db.ProtonConversationMessages.AsNoTracking().CountAsync(m => m.ConversationId == chat.ConversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task Chats_unused_for_the_retention_period_are_deleted()
    {
        var superAdmin = await SuperAdminAsync();
        api.Anthropic.Reply(FakeAnthropic.Text("Old news."), FakeAnthropic.Text("New news."));
        var old = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { text = "Anything old?" }));
        var recent = await OkAsync<ProtonReplyResponse>(AskAsync(superAdmin, new { text = "Anything new?" }));
        await _steps.ExecuteAsync("UPDATE proton_conversations SET updated_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 100 DAY) WHERE id = @id",
            ("@id", old.ConversationId.ToString()));

        await using var scope = api.Services.CreateAsyncScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<ProtonHistoryService>()
            .DeleteUntouchedSinceAsync(DateTime.UtcNow.AddDays(-90), CancellationToken.None);

        deleted.ShouldBeGreaterThanOrEqualTo(1);
        await (await Client.GetAsync($"/api/proton/conversations/{old.ConversationId}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await ChatAsync(superAdmin, recent.ConversationId)).Messages.Count.ShouldBe(2);
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        (await db.ProtonUsage.AsNoTracking().SingleAsync(u => u.ConversationId == old.ConversationId)).Question.ShouldBe("[deleted]");
    }
}
