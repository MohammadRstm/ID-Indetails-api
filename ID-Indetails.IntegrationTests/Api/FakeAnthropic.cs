using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Stands in for the Anthropic Messages API: answers each call with the next scripted response and keeps every request
/// body, so tests can check what Proton sent (tools offered, tool results) without calling a paid service.
/// </summary>
public sealed class FakeAnthropic : HttpMessageHandler
{
    private readonly ConcurrentQueue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<JsonNode> Requests { get; } = [];

    public void Reset()
    {
        _responses.Clear();
        lock (Requests)
        {
            Requests.Clear();
        }
    }

    public void Reply(params string[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue((HttpStatusCode.OK, response));
        }
    }

    public void Fail(HttpStatusCode status) => _responses.Enqueue((status, """{"type":"error","error":{"type":"api_error","message":"boom"}}"""));

    /// <summary>The tool names offered in request <paramref name="index"/>.</summary>
    public IReadOnlyList<string> ToolsOffered(int index)
        => Requests[index]["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();

    /// <summary>The tool results the last message of request <paramref name="index"/> carries.</summary>
    public IReadOnlyList<JsonNode> ToolResults(int index)
        => Requests[index]["messages"]!.AsArray().Last()!["content"]!.AsArray()
            .Where(b => b!["type"]!.GetValue<string>() == "tool_result")
            .Select(b => b!)
            .ToList();

    public static string ToolUse(string name, object input, string id = "toolu_1")
        => Message("tool_use", new { type = "tool_use", id, name, input });

    /// <param name="inputTokens">What the request "cost" in input tokens, to test limits on a conversation's size.</param>
    public static string Text(string text, int inputTokens = 1200) => Message("end_turn", new { type = "text", text }, inputTokens);

    /// <summary>A response made of several blocks, in order (for example the server's own tool search before a tool call).</summary>
    public static string Blocks(string stopReason, params object[] blocks) => Message(stopReason, blocks);

    private static string Message(string stopReason, object content, int inputTokens = 1200) => Message(stopReason, new[] { content }, inputTokens);

    private static string Message(string stopReason, object[] content, int inputTokens = 1200) => JsonSerializer.Serialize(new
    {
        id = "msg_" + Guid.NewGuid().ToString("N")[..12],
        type = "message",
        role = "assistant",
        model = "claude-sonnet-5-5",
        content,
        stop_reason = stopReason,
        stop_sequence = (string?)null,
        usage = new { input_tokens = inputTokens, output_tokens = 80, cache_creation_input_tokens = 0, cache_read_input_tokens = 400 },
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add(JsonNode.Parse(body)!);
        }

        var (status, response) = _responses.TryDequeue(out var next)
            ? next
            : (HttpStatusCode.InternalServerError, """{"type":"error","error":{"type":"api_error","message":"no scripted response"}}""");
        return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }
}
