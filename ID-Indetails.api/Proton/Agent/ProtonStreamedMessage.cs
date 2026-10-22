using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;

namespace ID_Indetails.api.Proton.Agent;

/// <summary>
/// Puts a streamed model reply back together as the <see cref="Message"/> a plain call would have returned, from the
/// stream's raw events: the opening message, each block's start, deltas and stop, and the closing delta with the stop
/// reason and the output usage. The same message then goes through the same tool loop and echo as before.
/// </summary>
internal sealed class ProtonStreamedMessage
{
    private JsonObject? _message;
    private readonly JsonArray _content = [];
    private readonly Dictionary<int, StringBuilder> _partialJson = [];

    /// <summary>
    /// The tokens the provider has reported so far: the input from the opening event, the output once a closing delta
    /// has come. Null before the stream has started.
    /// </summary>
    public Business.Proton.ProtonTokens? UsageSoFar
    {
        get
        {
            if (_message?["usage"] is not JsonObject usage)
            {
                return null;
            }

            int Count(string key) => usage[key] is JsonValue value && value.TryGetValue<int>(out var count) ? count : 0;
            return new Business.Proton.ProtonTokens(
                Count("input_tokens"), Count("output_tokens"), Count("cache_read_input_tokens"), Count("cache_creation_input_tokens"));
        }
    }

    public void Add(JsonElement streamEvent)
    {
        var json = JsonNode.Parse(streamEvent.GetRawText())!.AsObject();
        switch (json["type"]?.GetValue<string>())
        {
            case "message_start":
                _message = json["message"]!.DeepClone().AsObject();
                break;
            case "content_block_start":
                _content.Add(json["content_block"]!.DeepClone());
                break;
            case "content_block_delta":
                Delta(json["index"]!.GetValue<int>(), json["delta"]!.AsObject());
                break;
            case "content_block_stop":
                var index = json["index"]!.GetValue<int>();
                if (_partialJson.Remove(index, out var input))
                {
                    _content[index]!["input"] = InputOf(input);
                }

                break;
            case "message_delta":
                foreach (var (key, value) in json["delta"]!.AsObject())
                {
                    _message![key] = value?.DeepClone();
                }

                // the closing usage carries the final output count (and repeats the input counts)
                if (json["usage"] is JsonObject usage && _message!["usage"] is JsonObject total)
                {
                    foreach (var (key, value) in usage)
                    {
                        if (value is not null)
                        {
                            total[key] = value.DeepClone();
                        }
                    }
                }

                break;
        }
    }

    private void Delta(int index, JsonObject delta)
    {
        var block = _content[index]!.AsObject();
        switch (delta["type"]?.GetValue<string>())
        {
            case "text_delta":
                block["text"] = (block["text"]?.GetValue<string>() ?? "") + delta["text"]!.GetValue<string>();
                break;
            case "thinking_delta":
                block["thinking"] = (block["thinking"]?.GetValue<string>() ?? "") + delta["thinking"]!.GetValue<string>();
                break;
            case "signature_delta":
                block["signature"] = delta["signature"]!.GetValue<string>();
                break;
            case "input_json_delta":
                if (!_partialJson.TryGetValue(index, out var input))
                {
                    _partialJson[index] = input = new StringBuilder();
                }

                input.Append(delta["partial_json"]!.GetValue<string>());
                break;
            case "citations_delta":
                if (block["citations"] is not JsonArray citations)
                {
                    block["citations"] = citations = [];
                }

                citations.Add(delta["citation"]!.DeepClone());
                break;
        }
    }

    /// <summary>
    /// A tool call's input. One cut off at the output limit arrives half written: it is read as empty, and the agent
    /// drops the call with the rest of the unfinished reply rather than fail the question.
    /// </summary>
    private static JsonNode InputOf(StringBuilder input)
    {
        if (input.Length == 0)
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(input.ToString()) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    public Message ToMessage()
    {
        var message = _message ?? throw new InvalidOperationException("The model's stream ended before it started.");
        message["content"] = _content.DeepClone();
        return JsonSerializer.Deserialize<Message>(message.ToJsonString())
            ?? throw new InvalidOperationException("The model's streamed reply couldn't be read.");
    }
}
