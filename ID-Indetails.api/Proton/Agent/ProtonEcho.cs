using Anthropic.Models.Messages;

namespace ID_Indetails.api.Proton.Agent;

/// <summary>
/// A model response as the next request must repeat it: every block, in order and unchanged — thinking with its
/// signature, the server's own tool use and results (tool search), and the tool calls Proton answers. The SDK has no
/// conversion from response blocks to request blocks, so each kind Proton can receive is rebuilt here; a kind it
/// doesn't know fails the question instead of being dropped, since a dropped block breaks the conversation.
/// </summary>
internal static class ProtonEcho
{
    /// <summary>Only the reply's text, for a reply that can't be repeated whole: its non-empty text blocks, in order.</summary>
    public static List<ContentBlockParam> TextOf(Message response)
    {
        var text = new List<ContentBlockParam>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var written) && !string.IsNullOrWhiteSpace(written.Text))
            {
                text.Add(new TextBlockParam { Text = written.Text });
            }
        }

        return text;
    }

    public static (List<ContentBlockParam> Assistant, List<ToolUseBlock> ToolUses) Of(Message response)
    {
        var assistant = new List<ContentBlockParam>();
        var toolUses = new List<ToolUseBlock>();
        foreach (var block in response.Content)
        {
            if (block.TryPickThinking(out var thinking))
            {
                assistant.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                assistant.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickText(out var text))
            {
                assistant.Add(new TextBlockParam { Text = text.Text });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                assistant.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                toolUses.Add(toolUse);
            }
            else if (block.TryPickServerToolUse(out var serverToolUse))
            {
                assistant.Add(new ServerToolUseBlockParam { ID = serverToolUse.ID, Name = serverToolUse.Name.Raw(), Input = serverToolUse.Input });
            }
            else if (block.TryPickToolSearchToolResult(out var search))
            {
                assistant.Add(new ToolSearchToolResultBlockParam { ToolUseID = search.ToolUseID, Content = ContentOf(search.Content) });
            }
            else
            {
                throw new InvalidOperationException($"Proton received a {block.Json.GetProperty("type").GetString()} block it can't send back.");
            }
        }

        return (assistant, toolUses);
    }

    private static ToolSearchToolResultBlockParamContent ContentOf(ToolSearchToolResultBlockContent content)
    {
        if (content.TryPickToolSearchToolSearchResultBlock(out var result))
        {
            return new ToolSearchToolSearchResultBlockParam
            {
                ToolReferences = result.ToolReferences.Select(r => new ToolReferenceBlockParam { ToolName = r.ToolName }).ToList(),
            };
        }

        content.TryPickToolSearchToolResultError(out var error);
        return new ToolSearchToolResultErrorParam { ErrorCode = error!.ErrorCode };
    }
}
