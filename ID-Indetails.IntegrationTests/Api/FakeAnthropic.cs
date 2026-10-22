using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Stands in for the Anthropic Messages API: answers each call with the next scripted response and keeps every request
/// body, so tests can check what Proton sent (tools offered, tool results) without calling a paid service. A request
/// with <c>"stream": true</c> gets the same response as Server-Sent Events, the way the real API streams it.
/// </summary>
public sealed class FakeAnthropic : HttpMessageHandler
{
    private readonly ConcurrentQueue<(HttpStatusCode Status, string Body, TaskCompletionSource? Gate)> _responses = new();

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
            _responses.Enqueue((HttpStatusCode.OK, response, null));
        }
    }

    public void Fail(HttpStatusCode status) => _responses.Enqueue((status, """{"type":"error","error":{"type":"api_error","message":"boom"}}""", null));

    /// <summary>A response that waits until the returned gate opens, or the call is cancelled (the person pressed Stop).</summary>
    public TaskCompletionSource Hold(string response)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses.Enqueue((HttpStatusCode.OK, response, gate));
        return gate;
    }

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
        model = "claude-haiku-5-5",
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

        var (status, response, gate) = _responses.TryDequeue(out var next)
            ? next
            : (HttpStatusCode.InternalServerError, """{"type":"error","error":{"type":"api_error","message":"no scripted response"}}""", null);
        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        var streamed = status == HttpStatusCode.OK && JsonNode.Parse(body)?["stream"]?.GetValue<bool>() == true;
        return new HttpResponseMessage(status)
        {
            Content = streamed
                ? new StringContent(Sse(response), Encoding.UTF8, "text/event-stream")
                : new StringContent(response, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>A whole message as the stream of events the API sends for it: text and tool input arrive in pieces.</summary>
    private static string Sse(string messageJson)
    {
        var message = JsonNode.Parse(messageJson)!.AsObject();
        var content = message["content"]!.AsArray();
        var events = new StringBuilder();
        void Event(string type, JsonObject data)
        {
            data["type"] = type;
            events.Append($"event: {type}\ndata: {data.ToJsonString()}\n\n");
        }

        static IEnumerable<string> Halves(string text) => text.Length < 2 ? [text] : [text[..(text.Length / 2)], text[(text.Length / 2)..]];

        var start = message.DeepClone().AsObject();
        start["content"] = new JsonArray();
        start["stop_reason"] = null;
        Event("message_start", new JsonObject { ["message"] = start });
        for (var i = 0; i < content.Count; i++)
        {
            var block = content[i]!.AsObject();
            switch (block["type"]!.GetValue<string>())
            {
                case "text":
                    Event("content_block_start", new JsonObject { ["index"] = i, ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = "" } });
                    foreach (var piece in Halves(block["text"]!.GetValue<string>()))
                    {
                        Event("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = piece } });
                    }

                    break;
                case "tool_use" or "server_tool_use":
                    var opening = block.DeepClone().AsObject();
                    opening["input"] = new JsonObject();
                    Event("content_block_start", new JsonObject { ["index"] = i, ["content_block"] = opening });
                    foreach (var piece in Halves(block["input"]!.ToJsonString()))
                    {
                        Event("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = piece } });
                    }

                    break;
                case "thinking":
                    Event("content_block_start", new JsonObject { ["index"] = i, ["content_block"] = new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "" } });
                    Event("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = "thinking_delta", ["thinking"] = block["thinking"]!.GetValue<string>() } });
                    Event("content_block_delta", new JsonObject { ["index"] = i, ["delta"] = new JsonObject { ["type"] = "signature_delta", ["signature"] = block["signature"]!.GetValue<string>() } });
                    break;
                default:
                    Event("content_block_start", new JsonObject { ["index"] = i, ["content_block"] = block.DeepClone() });
                    break;
            }

            Event("content_block_stop", new JsonObject { ["index"] = i });
        }

        Event("message_delta", new JsonObject
        {
            ["delta"] = new JsonObject { ["stop_reason"] = message["stop_reason"]!.DeepClone(), ["stop_sequence"] = null },
            ["usage"] = new JsonObject { ["output_tokens"] = message["usage"]!["output_tokens"]!.DeepClone() },
        });
        Event("message_stop", new JsonObject());
        return events.ToString();
    }
}
