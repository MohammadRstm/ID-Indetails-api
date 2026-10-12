using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The streaming endpoint with a scripted model: the answer arrives as status, delta and done events; a refusal
/// before it starts keeps its status code; a failure after it is an error event in the uniform contract; and a
/// question stopped half-way is recorded, adds nothing to the chat, and leaves no change waiting for a Confirm.
/// </summary>
public class ProtonStreamTests(ProtonApiFactory api) : IClassFixture<ProtonApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record SseEvent(string Name, JsonNode Data);

    private Task<HttpResponseMessage> StreamAsync(AuthResponse who, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/proton/messages/stream")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", who.AccessToken);
        return Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static IEnumerable<SseEvent> Parse(string body)
        => body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk => chunk.Split('\n'))
            .Where(lines => lines[0].StartsWith("event: "))
            .Select(lines => new SseEvent(lines[0]["event: ".Length..], JsonNode.Parse(lines[1]["data: ".Length..])!));

    private async Task<AuthResponse> SuperAdminAsync()
    {
        api.Anthropic.Reset();
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        return superAdmin;
    }

    [Fact]
    public async Task The_answer_streams_as_status_and_text_then_the_same_reply_as_the_json_endpoint()
    {
        var superAdmin = await SuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(
            FakeAnthropic.ToolUse("list_projects", new { status = "active" }),
            FakeAnthropic.Text($"You have one active project: {project.Title}."));

        using var response = await StreamAsync(superAdmin, new { text = "Which projects are active?" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        response.Headers.GetValues("X-Accel-Buffering").ShouldBe(["no"]);
        var events = Parse(await response.Content.ReadAsStringAsync()).ToList();

        events.Select(e => e.Name).Distinct().ShouldBe(["status", "delta", "done"]);
        events.First(e => e.Name == "status").Data["text"]!.GetValue<string>().ShouldBe("Going through the projects…");
        string.Concat(events.Where(e => e.Name == "delta").Select(e => e.Data["text"]!.GetValue<string>()))
            .ShouldBe($"You have one active project: {project.Title}.");
        var done = events.Last().Data.Deserialize<ProtonReplyResponse>(Json)!;
        done.Reply.ShouldBe($"You have one active project: {project.Title}.");
        done.Refs.ShouldContain(r => r.Id == project.Id);

        // the model was asked to stream, with a cache breakpoint on the system prompt
        var first = api.Anthropic.Requests[0];
        first["stream"]!.GetValue<bool>().ShouldBeTrue();
        first["system"]!.AsArray().Single()!["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");

        // and the tool's streamed input arrived whole
        api.Anthropic.ToolResults(1).ShouldHaveSingleItem().ToJsonString().ShouldContain(project.Title);
    }

    [Fact]
    public async Task A_refusal_before_the_answer_starts_is_an_ordinary_error_response()
    {
        var superAdmin = await SuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await _steps.Client.PutJsonAsync($"/api/users/{employee.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.ProtonUse, action = "revoke", reason = "test" } } }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await StreamAsync(employee, new { text = "Hi" })).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await StreamAsync(superAdmin, new { text = "" })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await StreamAsync(superAdmin, new { conversationId = Guid.NewGuid(), text = "Hi" })).ShouldFailWithAsync(404, ErrorCodes.ProtonConversationEnded);
        api.Anthropic.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_after_the_answer_started_is_an_error_event_in_the_uniform_contract()
    {
        var superAdmin = await SuperAdminAsync();
        api.Anthropic.Reply(FakeAnthropic.ToolUse("list_projects", new { }));
        api.Anthropic.Fail(HttpStatusCode.InternalServerError);

        using var response = await StreamAsync(superAdmin, new { text = "Which projects are active?" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var events = Parse(await response.Content.ReadAsStringAsync()).ToList();
        events.First().Name.ShouldBe("status");
        var error = events.Last();
        error.Name.ShouldBe("error");
        error.Data["code"]!.GetValue<string>().ShouldBe(ErrorCodes.ProtonUnavailable);
        error.Data["type"]!.GetValue<string>().ShouldBe("unavailable");
        error.Data["traceId"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        events.ShouldNotContain(e => e.Name == "done");
    }

    [Fact]
    public async Task A_stopped_question_is_recorded_adds_nothing_to_the_chat_and_drops_its_proposal()
    {
        var superAdmin = await SuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        api.Anthropic.Reply(FakeAnthropic.Text("Hello! What can I do?"));
        var hello = await (await Client.PostJsonAsync("/api/proton/messages", new { text = "Hi" }, superAdmin.AccessToken)).ReadAsync<ProtonReplyResponse>();

        api.Anthropic.Reply(FakeAnthropic.ToolUse("propose_add_requirement", new { project_id = project.Id, text = "Check the plans" }));
        api.Anthropic.Hold(FakeAnthropic.Text("Confirm it on the card."));
        var response = await StreamAsync(superAdmin, new { conversationId = hello.ConversationId, text = "Add a requirement to check the plans" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdIndetailsDbContext>();
        await Eventually(() => db.ProtonActions.AsNoTracking().AnyAsync(a => a.UserId == superAdmin.User.Id));
        // the person presses Stop: the connection goes
        response.Dispose();

        await Eventually(() => db.ProtonUsage.AsNoTracking().AnyAsync(u => u.UserId == superAdmin.User.Id && u.Outcome == ProtonOutcome.Failed));
        (await db.ProtonActions.AsNoTracking().SingleAsync(a => a.UserId == superAdmin.User.Id)).Status.ShouldBe(ProtonActionStatus.Superseded);

        // nothing of the stopped question went into the chat: the next one follows straight on from "Hi"
        api.Anthropic.Reply(FakeAnthropic.Text("Still here."));
        var next = await Client.PostJsonAsync("/api/proton/messages", new { conversationId = hello.ConversationId, text = "Never mind" }, superAdmin.AccessToken);
        next.IsSuccessStatusCode.ShouldBeTrue(await next.Content.ReadAsStringAsync());
        api.Anthropic.Requests.Last()["messages"]!.AsArray().Count.ShouldBe(3);
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
