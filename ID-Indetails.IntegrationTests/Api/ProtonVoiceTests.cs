using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Talking to Proton with a fake voice provider and a scripted model: the browser gets the provider's answer and never
/// a key, one session a person, each spoken question goes to the same agent once, speech can't start a gated change,
/// and the server closes the session and records its time.
/// </summary>
public class ProtonVoiceTests(ProtonVoiceApiFactory api) : IClassFixture<ProtonVoiceApiFactory>
{
    private const string Offer = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\n";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<AuthResponse> SuperAdminAsync()
    {
        api.Anthropic.Reset();
        api.Voice.Reset();
        await NoSessionsAsync();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        return superAdmin;
    }

    /// <summary>Sessions are kept by person id, and ids repeat between tests: none may carry over.</summary>
    private async Task NoSessionsAsync()
    {
        var sessions = api.Services.GetRequiredService<ProtonVoiceSessions>();
        foreach (var session in sessions.All())
        {
            session.Close();
        }

        await Eventually(() => Task.FromResult(sessions.All().Count == 0));
    }

    private async Task<ProtonVoiceSessionResponse> StartAsync(AuthResponse who, Guid? conversationId = null)
    {
        var response = await Client.PostJsonAsync("/api/proton/voice/sessions", new { conversationId, sdp = Offer }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonVoiceSessionResponse>();
    }

    private Task<HttpResponseMessage> SayAsync(AuthResponse who, ProtonVoiceSessionResponse session, string text, string delegationId)
        => Client.PostJsonAsync("/api/proton/messages",
            new { conversationId = session.ConversationId, text, voice = new { sessionId = session.SessionId, delegationId } }, who.AccessToken);

    [Fact]
    public async Task The_browser_gets_the_providers_answer_and_never_the_key_and_one_session_a_person()
    {
        var superAdmin = await SuperAdminAsync();

        var response = await Client.PostJsonAsync("/api/proton/voice/sessions", new { sdp = Offer }, superAdmin.AccessToken);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldNotContain("voice-test-key");
        body.ShouldNotContain("test-key");
        var session = await response.ReadAsync<ProtonVoiceSessionResponse>();
        session.Sdp.ShouldStartWith("v=0");
        session.SessionId.ShouldStartWith("live_test_");

        // the provider was given the voice instructions and the browser's offer, nothing about the company
        var start = api.Voice.Starts.ShouldHaveSingleItem();
        start.SdpOffer.ShouldBe(Offer);
        start.Instructions.ShouldContain("delegate");
        start.UserId.ShouldBe(superAdmin.User.Id);

        await (await Client.PostJsonAsync("/api/proton/voice/sessions", new { sdp = Offer }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.ProtonVoiceBusy);

        // ending it: the server closes it with the provider and records the time it reported
        (await Client.PostJsonAsync($"/api/proton/voice/sessions/{session.SessionId}/end", new { }, superAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        await Eventually(() => db.ProtonUsage.AsNoTracking().AnyAsync(u => u.UserId == superAdmin.User.Id && u.VoiceSeconds != null));
        var row = await db.ProtonUsage.AsNoTracking().SingleAsync(u => u.UserId == superAdmin.User.Id && u.VoiceSeconds != null);
        row.Channel.ShouldBe(ProtonChannel.Voice);
        row.VoiceSeconds.ShouldBe(42);
        row.Model.ShouldBe("gpt-live-1");
        row.Outcome.ShouldBe(ProtonOutcome.Answered);
        row.CostUsd.ShouldBe(0.035m);
        row.ToolCalls.ShouldBe(0);
        api.Voice.Links[session.SessionId].Sent.ShouldContain(c => c["type"]!.GetValue<string>() == "session.close");

        // a session is not a question
        var usage = await (await Client.GetAsync("/api/proton/usage", superAdmin.AccessToken)).ReadAsync<ProtonUsageResponse>();
        usage.QuestionsUsed.ShouldBe(0);
        usage.VoiceAvailable.ShouldBeTrue();

        // and once it has ended, another can start; ending one twice is fine
        (await Client.PostJsonAsync($"/api/proton/voice/sessions/{session.SessionId}/end", new { }, superAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await StartAsync(superAdmin);
        await NoSessionsAsync();
    }

    [Fact]
    public async Task A_spoken_question_goes_to_the_same_conversation_once_and_is_marked_as_spoken()
    {
        var superAdmin = await SuperAdminAsync();
        api.Anthropic.Reply(FakeAnthropic.Text("You have no projects yet."), FakeAnthropic.Text("Still none."));
        var session = await StartAsync(superAdmin);

        var response = await SayAsync(superAdmin, session, "How many projects do we have?", "item_1");
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var reply = await response.ReadAsync<ProtonReplyResponse>();
        reply.ConversationId.ShouldBe(session.ConversationId);
        reply.Reply.ShouldBe("You have no projects yet.");
        api.Anthropic.Requests[0].ToJsonString().ShouldContain("voice conversation");

        // delivered again (a retry, a reconnect): not asked twice
        await (await SayAsync(superAdmin, session, "How many projects do we have?", "item_1"))
            .ShouldFailWithAsync(409, ErrorCodes.ProtonVoiceRepeated);
        api.Anthropic.Requests.Count.ShouldBe(1);

        // typed next, in the same conversation: it carries on
        var typed = await Client.PostJsonAsync("/api/proton/messages", new { conversationId = session.ConversationId, text = "And now?" }, superAdmin.AccessToken);
        (await typed.ReadAsync<ProtonReplyResponse>()).Reply.ShouldBe("Still none.");

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        var questions = await db.ProtonUsage.AsNoTracking()
            .Where(u => u.UserId == superAdmin.User.Id && u.VoiceSeconds == null).OrderBy(u => u.Id).Select(u => u.Channel).ToListAsync();
        questions.ShouldBe([ProtonChannel.Voice, ProtonChannel.Text]);
        // the saved chat keeps how each question was asked
        var saved = await db.ProtonConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == session.ConversationId && m.Role == ProtonMessageRole.User).OrderBy(m => m.Seq).Select(m => m.Origin).ToListAsync();
        saved.ShouldBe([ProtonMessageOrigin.Spoken, ProtonMessageOrigin.Typed]);
        await NoSessionsAsync();
    }

    [Fact]
    public async Task Only_the_owner_asks_in_a_running_session_and_an_ended_one_takes_no_questions()
    {
        var superAdmin = await SuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var session = await StartAsync(superAdmin);

        // someone else naming this session, from a conversation of their own
        api.Anthropic.Reply(FakeAnthropic.Text("Hello."));
        var own = await (await Client.PostJsonAsync("/api/proton/messages", new { text = "Hi" }, admin.AccessToken)).ReadAsync<ProtonReplyResponse>();
        await (await Client.PostJsonAsync("/api/proton/messages",
                new { conversationId = own.ConversationId, text = "Hi", voice = new { sessionId = session.SessionId, delegationId = "item_1" } }, admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.ProtonVoiceUnavailable);
        (await Client.PostJsonAsync($"/api/proton/voice/sessions/{session.SessionId}/end", new { }, admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.Voice.Links[session.SessionId].Sent.ShouldBeEmpty();

        // the provider ends it (the person hung up): the server records it and takes no more of its questions
        api.Voice.Links[session.SessionId].Push(new JsonObject
        {
            ["type"] = "session.closed",
            ["reason"] = "remote_hangup",
            ["usage"] = new JsonObject { ["seconds"] = 20 },
        });
        await NoSessionsAsync();
        await (await SayAsync(superAdmin, session, "Anything new?", "item_2")).ShouldFailWithAsync(404, ErrorCodes.ProtonVoiceUnavailable);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        await Eventually(() => db.ProtonUsage.AsNoTracking().AnyAsync(u => u.UserId == superAdmin.User.Id && u.VoiceSeconds == 20));
    }

    [Fact]
    public async Task Speech_can_propose_an_everyday_change_on_a_card_but_not_a_gated_one()
    {
        var superAdmin = await SuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var session = await StartAsync(superAdmin);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("propose_send_message", new { }),
            FakeAnthropic.ToolUse("propose_add_requirement", new { project_id = project.Id, text = "Check the hours" }, id: "toolu_2"),
            FakeAnthropic.Text("I've put a card on your screen."));

        var reply = await (await SayAsync(superAdmin, session, "Message the team and add a requirement to check the hours", "item_1"))
            .ReadAsync<ProtonReplyResponse>();

        var refused = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        refused["status"]!.GetValue<string>().ShouldBe("refused");
        refused["message"]!.GetValue<string>().ShouldContain("by voice");
        JsonNode.Parse(api.Anthropic.ToolResults(2).Single()["content"]!.GetValue<string>())!["status"]!.GetValue<string>().ShouldBe("proposed");

        // only a card: nothing changes until the person presses Confirm on it
        reply.PendingAction.ShouldNotBeNull();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        (await db.ProtonActions.AsNoTracking().SingleAsync(a => a.UserId == superAdmin.User.Id)).Status.ShouldBe(ProtonActionStatus.Pending);
        await NoSessionsAsync();
    }

    [Fact]
    public async Task A_refused_session_frees_the_persons_place_and_a_bad_offer_is_refused_before_the_provider()
    {
        var superAdmin = await SuperAdminAsync();

        await (await Client.PostJsonAsync("/api/proton/voice/sessions", new { sdp = "not an offer" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/proton/messages",
                new { text = "Hi", voice = new { sessionId = "bad id!", delegationId = "item_1" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        api.Voice.Refuse = true;
        await (await Client.PostJsonAsync("/api/proton/voice/sessions", new { sdp = Offer }, superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.ProtonVoiceUnavailable);
        api.Voice.Refuse = false;
        await StartAsync(superAdmin);
        api.Voice.Starts.Count.ShouldBe(1);
        await NoSessionsAsync();
    }

    [Fact]
    public async Task Without_Proton_access_there_is_no_voice_either()
    {
        var superAdmin = await SuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ProtonUse, action = "revoke", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.PostJsonAsync("/api/proton/voice/sessions", new { sdp = Offer }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        api.Voice.Starts.ShouldBeEmpty();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("It didn't happen within 10 seconds.");
    }
}

/// <summary>With voice switched off, starting a session says so and nothing is asked of the provider.</summary>
public class ProtonVoiceOffTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    [Fact]
    public async Task Voice_switched_off_is_unavailable_and_the_usage_says_so()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await _steps.Client.PostJsonAsync("/api/proton/voice/sessions", new { sdp = "v=0\r\n" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(503, ErrorCodes.ProtonVoiceUnavailable);
        var usage = await (await _steps.Client.GetAsync("/api/proton/usage", superAdmin.AccessToken)).ReadAsync<ProtonUsageResponse>();
        usage.VoiceAvailable.ShouldBeFalse();
        usage.VoiceMaxMinutes.ShouldBe(0);
    }
}
