using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ProtonUsage = ID_Indetails.api.Models.ProtonUsage;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Prompts;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Agent;

/// <summary>
/// The text chat with Proton on Claude: one question in, one answer out. The model chooses the tools; the toolbox
/// decides whether the user may use them and runs them read-only through the existing services. Only the chat lives
/// here — a live-voice interface would be another agent over the same <see cref="ProtonToolbox"/>.
/// </summary>
public sealed class ProtonChatAgent(
    IAnthropicClient anthropic,
    ProtonToolbox toolbox,
    ProtonConversationStore conversations,
    ProtonUsageService usage,
    IPermissionService permissions,
    IClock clock,
    IOptions<ProtonOptions> options,
    ILoggerFactory loggers)
{


    private readonly ILogger _log = loggers.CreateLogger(ProtonToolbox.LogCategory);

    public async Task<ProtonReplyResponse> AskAsync(
        ClaimsPrincipal caller, long userId, AskProtonRequest request, CancellationToken cancellationToken)
    {
        await usage.EnsureCanAskAsync(userId, cancellationToken);

        var access = await permissions.GetAccessAsync(userId, cancellationToken);
        var conversation = OpenConversation(userId, request.ConversationId, Fingerprint(access));
        if (!conversation.TryBegin())
        {
            throw new ConflictException(ErrorCodes.ProtonBusy, "Proton is still answering your last question.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.QuestionTimeoutSeconds));
            var turn = await RunTurnAsync(caller, access, conversation, request.Text, timeout.Token);

            await usage.RecordAsync(turn.Record(userId, conversation.Id, request.Text, clock.UtcNow, usage.Prices), CancellationToken.None);
            if (turn.Outcome == ProtonOutcome.Failed)
            {
                throw new ServiceUnavailableException(ErrorCodes.ProtonUnavailable, "Proton couldn't answer right now. Please try again.");
            }

            conversation.Messages.AddRange(turn.Messages);
            conversation.Questions++;
            conversation.LastInputTokens = turn.LastInputTokens;
            return new ProtonReplyResponse(
                conversation.Id,
                turn.Answer,
                turn.RefsIn(turn.Answer),
                turn.Outcome == ProtonOutcome.Limited,
                await usage.MineAsync(userId, CancellationToken.None),
                turn.Sources(),
                // One proposal at a time: a later one in the same answer has already replaced earlier ones.
                turn.ToolCalls.LastOrDefault(t => t.Pending is not null)?.Pending);
        }
        finally
        {
            conversation.End();
        }
    }

    public void Forget(long userId, Guid conversationId) => conversations.Remove(userId, conversationId);

    private ProtonConversation OpenConversation(long userId, Guid? conversationId, string fingerprint)
    {
        if (conversationId is not { } id)
        {
            return conversations.Start(userId, fingerprint);
        }

        var conversation = conversations.Find(userId, id)
            ?? throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "This conversation has ended. Start a new one.");
        if (conversation.AccessFingerprint != fingerprint)
        {
            // Earlier answers were given under different permissions: they must not carry over.
            conversations.Remove(userId, id);
            throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "Your access changed, so this conversation was closed. Start a new one.");
        }

        if (conversation.Questions >= options.Value.MaxQuestionsPerConversation)
        {
            throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "This conversation is full. Start a new one.");
        }

        if (conversation.LastInputTokens > options.Value.MaxConversationInputTokens)
        {
            throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "This conversation has grown long. Start a new one to keep answers quick.");
        }

        return conversation;
    }

    private async Task<ProtonTurn> RunTurnAsync(
        ClaimsPrincipal caller, UserAccess access, ProtonConversation conversation, string text, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var turn = new ProtonTurn(settings.Model);
        try
        {
            var tools = await toolbox.OpenAsync(
                caller, access.UserId, new ProtonOrigin(conversation.Id, settings.Model, ProtonPrompts.ChatVersion), cancellationToken);
            var definitions = tools.Tools.Select(Define).ToList();
            turn.Messages.Add(new MessageParam
            {
                Role = Role.User,
                Content = new List<ContentBlockParam>
                {
                    new TextBlockParam { Text = ContextNote(caller, access, conversation) },
                    new TextBlockParam { Text = text },
                },
            });

            for (var trip = 1; trip <= settings.MaxRoundTrips; trip++)
            {
                var lastTrip = trip == settings.MaxRoundTrips || turn.ToolCalls.Count >= settings.MaxToolCallsPerQuestion;
                var response = await anthropic.Messages.Create(new MessageCreateParams
                {
                    Model = settings.Model,
                    MaxTokens = settings.MaxOutputTokens,
                    System = ProtonPrompts.Chat,
                    Messages = [.. conversation.Messages, .. turn.Messages],
                    Tools = definitions,
                    ToolChoice = lastTrip ? new ToolChoiceNone() : new ToolChoiceAuto(),
                    Thinking = new ThinkingConfigAdaptive(),
                    OutputConfig = new OutputConfig { Effort = Enum.Parse<Effort>(settings.Effort, ignoreCase: true) },
                    CacheControl = new CacheControlEphemeral(),
                }, cancellationToken);
                turn.Count(response);

                var (assistant, toolUses) = Echo(response);
                turn.Messages.Add(new MessageParam { Role = Role.Assistant, Content = assistant });

                if (response.StopReason == StopReason.ToolUse && toolUses.Count > 0 && !lastTrip)
                {
                    turn.Messages.Add(new MessageParam
                    {
                        Role = Role.User,
                        Content = await RunToolsAsync(tools, toolUses, turn, conversation.Focus, cancellationToken),
                    });
                    continue;
                }

                turn.Finish(response, Answer(response), lastTrip);
                return turn;
            }

            turn.Fail("No answer within the round-trip limit.");
            return turn;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning("Proton ran out of time after {RoundTrips} model calls and {ToolCalls} tool calls.", turn.RoundTrips, turn.ToolCalls.Count);
            turn.Fail("Out of time.");
            return turn;
        }
        catch (Exception exception)
        {
            // Whatever failed (the provider, the network, a bug), the tokens already used are still recorded.
            _log.LogError(exception, "Proton failed after {RoundTrips} model calls.", turn.RoundTrips);
            turn.Fail(exception is AnthropicException or HttpRequestException or OperationCanceledException ? "The model call failed." : "Proton failed.");
            return turn;
        }
    }

    private async Task<List<ContentBlockParam>> RunToolsAsync(
        ProtonToolSession tools, IReadOnlyList<ToolUseBlock> toolUses, ProtonTurn turn, ConversationFocus focus, CancellationToken cancellationToken)
    {
        var results = new List<ContentBlockParam>();
        foreach (var toolUse in toolUses)
        {
            var input = JsonSerializer.SerializeToElement(toolUse.Input);
            ProtonToolResult result;
            if (turn.ToolCalls.Count >= options.Value.MaxToolCallsPerQuestion)
            {
                result = new ProtonToolResult(toolUse.Name, input, ProtonToolStatus.Refused,
                    """{"status":"refused","message":"Tool limit reached for this question: answer with what you have."}""", [], null, 0);
            }
            else
            {
                // One at a time: the tools share the request's database context.
                result = await tools.RunAsync(toolUse.Name, input, cancellationToken);
                focus.Take(result);
            }

            turn.ToolCalls.Add(result);
            results.Add(new ToolResultBlockParam
            {
                ToolUseID = toolUse.ID,
                Content = result.Content,
                IsError = result.Status != ProtonToolStatus.Ok,
            });
        }

        return results;
    }

    /// <summary>The response as the next request must repeat it: every block, thinking included and unchanged.</summary>
    private static (List<ContentBlockParam> Assistant, List<ToolUseBlock> ToolUses) Echo(Message response)
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
        }

        return (assistant, toolUses);
    }

    private static string Answer(Message response)
    {
        if (response.StopReason == StopReason.Refusal)
        {
            return "I can't help with that one.";
        }

        var text = string.Join("\n\n", response.Content
            .Select(b => b.TryPickText(out var t) ? t.Text.Trim() : null)
            .Where(t => !string.IsNullOrEmpty(t)));
        return string.IsNullOrWhiteSpace(text) ? "I couldn't put an answer together. Please try asking another way." : text;
    }

    private static ToolUnion Define(IProtonTool tool)
    {
        var schema = tool.InputSchema;
        return new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new InputSchema
            {
                Properties = schema["properties"]?.AsObject()
                    .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)) ?? [],
                Required = schema["required"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? [],
            },
        };
    }

    /// <summary>
    /// Facts the model needs each question, written by Hourline (not the user): today in the company's time zone, who
    /// is asking and what they can see, and what the conversation is about. Sent with the question rather than in the
    /// system prompt, so the prompt stays the same for everyone and is cached.
    /// </summary>
    private string ContextNote(ClaimsPrincipal caller, UserAccess access, ProtonConversation conversation)
    {
        var name = $"{caller.FindFirst(AuthClaimTypes.FirstName)?.Value} {caller.FindFirst(AuthClaimTypes.LastName)?.Value}".Trim();
        var today = clock.CompanyToday;
        var actions = conversation.TakeActionNotes();
        var note = $"""
            [Context from Hourline — not written by the user]
            Today: {today.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)} (company time zone).
            Asking: {(name.Length > 0 ? name : "a user")}, {access.Role.Replace('_', ' ')}, who can see: {string.Join(", ", access.VisibleDomains)}.
            Conversation focus: {conversation.Focus.Describe()}.
            """;
        return actions.Count == 0 ? note : note + "\nSince the last message (from the confirmation buttons): " + string.Join(" ", actions);
    }

    /// <summary>What a conversation's answers depended on: if any of it changes, the conversation ends.</summary>
    private static string Fingerprint(UserAccess access)
        => string.Join('|', access.Role, access.Domain, string.Join(',', access.VisibleDomains), string.Join(',', access.Permissions.Order(StringComparer.Ordinal)));
}

/// <summary>One question's progress: the messages it adds, the tools it ran, the tokens it used and how it ended.</summary>
internal sealed class ProtonTurn(string model)
{
    private const int MaxAnswerLength = 8_000;

    private ProtonTokens _tokens = ProtonTokens.None;

    public List<MessageParam> Messages { get; } = [];

    public List<ProtonToolResult> ToolCalls { get; } = [];

    public int RoundTrips { get; private set; }

    /// <summary>Input tokens of the last model call, cached ones included.</summary>
    public int LastInputTokens { get; private set; }

    public ProtonOutcome Outcome { get; private set; } = ProtonOutcome.Failed;

    public string Answer { get; private set; } = string.Empty;

    public void Count(Message response)
    {
        RoundTrips++;
        var u = response.Usage;
        LastInputTokens = (int)(u.InputTokens + (u.CacheReadInputTokens ?? 0) + (u.CacheCreationInputTokens ?? 0));
        _tokens = _tokens.Add(new ProtonTokens(
            (int)u.InputTokens, (int)u.OutputTokens, (int)(u.CacheReadInputTokens ?? 0), (int)(u.CacheCreationInputTokens ?? 0)));
    }

    public void Finish(Message response, string answer, bool atLimit)
    {
        Answer = answer;
        Outcome = response.StopReason == StopReason.Refusal ? ProtonOutcome.Refused
            : response.StopReason == StopReason.MaxTokens || atLimit ? ProtonOutcome.Limited
            : ProtonOutcome.Answered;
    }

    public void Fail(string reason)
    {
        Outcome = ProtonOutcome.Failed;
        Answer = $"[failed: {reason}]";
    }

    /// <summary>The analyses the answer rests on, once each, for "Based on" under it.</summary>
    public IReadOnlyList<ProtonSourceResponse> Sources()
        => ToolCalls
            .Where(t => t.Status == ProtonToolStatus.Ok && t.Basis is not null)
            .Select(t => t.Basis!)
            .Select(b => new ProtonSourceResponse(
                b.Label, b.Period is { } p ? Days(p) : null, b.ComparedWith is { } c ? Days(c) : null, b.Partial, b.Scope, b.Omitted))
            .DistinctBy(s => (s.Label, s.Period, s.ComparedWith))
            .ToList();

    /// <summary>Days in words: "7 Oct 2026", "1–7 Oct 2026", "28 Sep – 4 Oct 2026", "28 Dec 2025 – 3 Jan 2026".</summary>
    public static string Days(DayRange range)
    {
        var (from, to) = (range.From, range.To);
        string Day(DateOnly d, string format) => d.ToString(format, CultureInfo.InvariantCulture);
        return from == to ? Day(to, "d MMM yyyy")
            : from.Year != to.Year ? $"{Day(from, "d MMM yyyy")} – {Day(to, "d MMM yyyy")}"
            : from.Month != to.Month ? $"{Day(from, "d MMM")} – {Day(to, "d MMM yyyy")}"
            : $"{from.Day}–{Day(to, "d MMM yyyy")}";
    }

    /// <summary>The records the tools returned that the answer actually names, for the client to link.</summary>
    public IReadOnlyList<ProtonRef> RefsIn(string answer)
        => ToolCalls.SelectMany(t => t.Refs)
            .DistinctBy(r => (r.Type, r.Id))
            .Where(r => r.Name.Length > 1 && answer.Contains(r.Name, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

    public ProtonUsage Record(long userId, Guid conversationId, string question, DateTime at, ProtonTokenPrices prices) => new()
    {
        UserId = userId,
        ConversationId = conversationId,
        OccurredAt = at,
        Model = model,
        PromptVersion = ProtonPrompts.ChatVersion,
        Outcome = Outcome,
        RoundTrips = RoundTrips,
        ToolCalls = ToolCalls.Count,
        InputTokens = _tokens.Input,
        OutputTokens = _tokens.Output,
        CacheReadTokens = _tokens.CacheRead,
        CacheWriteTokens = _tokens.CacheWrite,
        CostUsd = ProtonUsageRules.CostOf(_tokens, prices),
        Question = question,
        Answer = ProtonText.Cut(Answer, MaxAnswerLength),
        Tools = JsonSerializer.Serialize(ToolCalls.Select(t => new { tool = t.Tool, input = t.Input, status = t.Status.ToString(), ms = t.ElapsedMs }), ProtonJson.Options),
    };
}
