using System.Diagnostics;
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
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
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
    ProtonRecords records,
    IProtonActionRepository actions,
    ICompanySettingsService companySettings,
    IClock clock,
    IOptions<ProtonOptions> options,
    IOptions<EmailOptions> email,
    ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger(ProtonToolbox.LogCategory);

    /// <summary>A question on the JSON endpoint: the whole reply at once.</summary>
    public async Task<ProtonReplyResponse> AskAsync(
        ClaimsPrincipal caller, long userId, AskProtonRequest request, CancellationToken cancellationToken)
    {
        await using var question = await PrepareAsync(caller, userId, request, cancellationToken);
        return await AnswerAsync(question, null, options.Value.QuestionTimeoutSeconds, cancellationToken);
    }

    /// <summary>
    /// Everything that can refuse a question, before any of its answer is sent: Proton switched on, the allowance and
    /// budget, the conversation (ended, someone else's, access changed, still busy), and a button that wasn't offered.
    /// The question holds its conversation until it is disposed.
    /// </summary>
    public async Task<ProtonQuestion> PrepareAsync(
        ClaimsPrincipal caller, long userId, AskProtonRequest request, CancellationToken cancellationToken)
    {
        await usage.EnsureCanAskAsync(userId, cancellationToken);

        var access = await permissions.GetAccessAsync(userId, cancellationToken);
        var conversation = OpenConversation(userId, request.ConversationId, Fingerprint(access));
        if (!conversation.TryBegin())
        {
            throw new ConflictException(ErrorCodes.ProtonBusy, "I'm still answering your last question.");
        }

        try
        {
            // Too long to carry on: the chat starts afresh under the same id, with a recap, instead of ending.
            var rolledOver = conversation.Questions >= options.Value.MaxQuestionsPerConversation
                || conversation.LastInputTokens > options.Value.MaxConversationInputTokens;
            if (rolledOver)
            {
                _log.LogInformation("Proton conversation {ConversationId} rolled over after {Questions} questions and {InputTokens} input tokens.",
                    conversation.Id, conversation.Questions, conversation.LastInputTokens);
                conversation.RollOver();
            }

            var tools = await toolbox.OpenAsync(
                caller, userId, new ProtonOrigin(conversation.Id, options.Value.Model, ProtonPrompts.ChatVersion), cancellationToken);
            tools.Context.Remember(conversation.Known.Values);
            tools.Context.ActionPending = conversation.HasPendingAction(clock.UtcNow);
            // known from the request's shape, never from its text
            tools.Context.Turn = request switch
            {
                { Choice: not null } or { Reply: not null } => ProtonTurnOrigin.Choice,
                { Suggestion: not null } => ProtonTurnOrigin.Suggestion,
                _ => ProtonTurnOrigin.Typed,
            };
            // A button becomes a question written by the app, once it is known to be one the last reply offered.
            var text = request switch
            {
                { Choice: { } choice } => await ChosenAsync(choice, conversation, tools.Context, cancellationToken),
                { Reply: { } reply } => Replied(reply, conversation),
                { Suggestion: { } index } => Suggested(index, conversation),
                _ => request.Text,
            };
            var page = request.Page is { } at ? await PageNoteAsync(at, tools.Context, cancellationToken) : null;
            var company = (await companySettings.BrandingAsync(cancellationToken)).Name;
            var notes = new ProtonNotes(page, company, tools.Context.Turn == ProtonTurnOrigin.Suggestion);
            return new ProtonQuestion(caller, userId, access, conversation, tools, request, text, notes, rolledOver);
        }
        catch
        {
            conversation.End();
            throw;
        }
    }

    /// <summary>Answers a prepared question within the time given, telling <paramref name="listener"/> how it goes when there is one.</summary>
    public async Task<ProtonReplyResponse> AnswerAsync(
        ProtonQuestion question, IProtonListener? listener, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var (conversation, tools, request, text) = (question.Conversation, question.Tools, question.Request, question.Text);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var turn = await RunTurnAsync(question.Caller, question.Access, conversation, tools, text, question.Notes, listener, timeout.Token);

        if (turn.Outcome == ProtonOutcome.Failed)
        {
            await DropProposalsAsync(turn);
        }

        var record = turn.Record(question.UserId, conversation.Id, text, clock.UtcNow, usage.Prices);
        await usage.RecordAsync(record, CancellationToken.None);
        // what a question costs in time and money, to measure before Proton grows (no question or answer text)
        _log.LogInformation(
            "Proton question {Outcome} in {WallMs} ms (first model reply after {FirstReplyMs} ms): {RoundTrips} model calls, " +
            "{ToolCalls} tool calls taking {ToolMs} ms, {InputTokens} input and {OutputTokens} output tokens, about {CostUsd} USD.",
            record.Outcome, turn.ElapsedMs, turn.FirstReplyMs, record.RoundTrips, record.ToolCalls, turn.ToolCalls.Sum(t => t.ElapsedMs),
            record.InputTokens + record.CacheReadTokens + record.CacheWriteTokens, record.OutputTokens, record.CostUsd);
        if (turn.Outcome == ProtonOutcome.Failed)
        {
            throw new ServiceUnavailableException(ErrorCodes.ProtonUnavailable, "Sorry, I couldn't answer that just now. Please try again.");
        }

        conversation.Messages.AddRange(turn.Messages);
        conversation.RememberExchange(text, turn.Answer);
        conversation.Questions++;
        conversation.LastInputTokens = turn.LastInputTokens;
        foreach (var known in tools.Context.Known)
        {
            conversation.Known[(known.Type, known.Id)] = known;
        }

        var ok = turn.ToolCalls.Where(t => t.Status == ProtonToolStatus.Ok).ToList();
        // One proposal at a time: a later one in the same answer has already replaced earlier ones.
        var pending = turn.ToolCalls.LastOrDefault(t => t.Pending is not null)?.Pending;
        var choices = ok.LastOrDefault(t => t.Choices is not null)?.Choices ?? [];
        var replies = ok.LastOrDefault(t => t.QuickReplies is not null)?.QuickReplies ?? [];
        var suggestions = ok.LastOrDefault(t => t.Suggestions is not null)?.Suggestions ?? [];
        if (pending is not null)
        {
            // offered before the proposal in the same answer: next to a card, nothing may pass for its Confirm
            conversation.Proposed(pending.Id, pending.ExpiresAt);
            replies = replies.Where(r => !ProtonUiRules.IsYesOrNo(r.Reply)).ToList();
            suggestions = [];
        }

        if (choices.Count > 0 || replies.Count > 0 || request.Choice is not null || request.Reply is not null)
        {
            conversation.OfferedChoices = choices;
            conversation.OfferedReplies = replies.Select(r => r.Reply).ToList();
        }

        conversation.OfferedSuggestions = suggestions;

        return new ProtonReplyResponse(
            conversation.Id,
            turn.Answer,
            turn.RefsIn(turn.Answer),
            turn.Outcome == ProtonOutcome.Limited,
            await usage.MineAsync(question.UserId, CancellationToken.None),
            turn.Sources(),
            ProtonUiRules.Pick(ok.Select(t => t.UiCommand).OfType<ProtonUiCommand>()),
            choices,
            replies,
            suggestions,
            pending,
            question.RolledOver);
    }

    /// <summary>
    /// A question that failed or was stopped never showed its card, so a change it proposed must not stay waiting for a
    /// Confirm that can't come.
    /// </summary>
    private async Task DropProposalsAsync(ProtonTurn turn)
    {
        foreach (var pending in turn.ToolCalls.Select(t => t.Pending).OfType<Actions.ProtonPendingAction>())
        {
            await actions.CloseAsync(pending.Id, ProtonActionStatus.Superseded, clock.UtcNow, CancellationToken.None);
            _log.LogInformation("Proton action {ActionId} dropped: its question didn't finish.", pending.Id);
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
            ?? throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "I no longer have our earlier chat, so we're starting fresh.");
        if (conversation.AccessFingerprint != fingerprint)
        {
            // Earlier answers were given under different permissions: they must not carry over, not even as a recap.
            conversations.Remove(userId, id);
            throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "Your access changed, so I closed our earlier chat and we're starting fresh.");
        }

        return conversation;
    }

    /// <summary>
    /// The option the user picked, as a question the app writes for them: it must be one of the last offered options and
    /// still visible to the user now. Anything else is refused, so the client can't make Proton act on a record it chose.
    /// </summary>
    private async Task<string> ChosenAsync(
        ProtonChoiceRequest choice, ProtonConversation conversation, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var offered = conversation.OfferedChoices.Any(c => c.Type == choice.Type && c.Id == choice.Id);
        var record = offered ? await records.FindAsync(choice.Type, choice.Id, context, cancellationToken) : null;
        if (record is null)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonChoiceInvalid, "That option isn't available any more. Could you ask me again?");
        }

        conversation.OfferedChoices = [];
        conversation.OfferedReplies = [];
        return $"[The user picked this from the options you offered] {Describe(record)}.";
    }

    /// <summary>A quick reply, in the app's words, once it is known to be one the last reply offered.</summary>
    private string Replied(ProtonQuickReply reply, ProtonConversation conversation)
    {
        if (!conversation.OfferedReplies.Contains(reply) || (ProtonUiRules.IsYesOrNo(reply) && conversation.HasPendingAction(clock.UtcNow)))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonChoiceInvalid, "That option isn't available any more. Could you ask me again?");
        }

        conversation.OfferedChoices = [];
        conversation.OfferedReplies = [];
        return $"[The user picked this from the options you offered] {ProtonUiRules.Option(reply).Label}.";
    }

    /// <summary>The suggested reply the user tapped, taken from what this conversation offered: the client sends only its place.</summary>
    private static string Suggested(int index, ProtonConversation conversation)
    {
        if (index < 0 || index >= conversation.OfferedSuggestions.Count)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonChoiceInvalid, "That suggestion isn't available any more. Could you type it instead?");
        }

        var text = conversation.OfferedSuggestions[index];
        conversation.OfferedSuggestions = [];
        return text;
    }

    /// <summary>
    /// Where the user is, for the context note: the page, and the record on it only when this user may see it, so an
    /// id typed into the address bar never becomes a name. Context only: it is neither data nor permission.
    /// </summary>
    private async Task<string> PageNoteAsync(ProtonPageContext page, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var where = page.Page is { } known ? $"the {Words(known)} page" : "a page";
        var note = $"{where} in {(page.Area == "company" ? "the company area" : page.Area)}";
        if (page.EntityType is { } type && page.EntityId is { } id
            && await records.FindAsync(type, id, context, cancellationToken) is { } record)
        {
            note += $", looking at {Describe(record)}" + (page.Section is { } section and not ProtonProjectSection.Overview ? $" ({Words(section)} tab)" : "");
        }

        if (page.Surface is { } surface)
        {
            note += $", with {ProtonSurfaces.All[surface].Label} open";
        }

        return note;
    }

    private static string Describe(ProtonRef record)
        => $"{record.Type.ToString().ToLowerInvariant()} #{record.Id} \"{record.Name}\"" + (record.Domain is { } d ? $" ({d})" : "");

    private static string Words<T>(T value) where T : struct, Enum
        => string.Concat(value.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));

    private async Task<ProtonTurn> RunTurnAsync(
        ClaimsPrincipal caller, UserAccess access, ProtonConversation conversation, ProtonToolSession tools, string text, ProtonNotes notes,
        IProtonListener? listener, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var turn = new ProtonTurn(settings.Model);
        try
        {
            var definitions = tools.Tools.Select(Define).ToList();
            // a breakpoint on the system prompt caches the tools and the prompt for everyone; the automatic one below
            // caches the conversation so far
            var system = new List<TextBlockParam>
            {
                new() { Text = ProtonPrompts.ChatFor(email.Value.ProductName), CacheControl = new CacheControlEphemeral() },
            };
            turn.Messages.Add(new MessageParam
            {
                Role = Role.User,
                Content = new List<ContentBlockParam>
                {
                    new TextBlockParam { Text = ContextNote(caller, access, conversation, notes) },
                    new TextBlockParam { Text = text },
                },
            });

            for (var trip = 1; trip <= settings.MaxRoundTrips; trip++)
            {
                var lastTrip = trip == settings.MaxRoundTrips || turn.ToolCalls.Count >= settings.MaxToolCallsPerQuestion;
                var request = new MessageCreateParams
                {
                    Model = settings.Model,
                    MaxTokens = settings.MaxOutputTokens,
                    System = system,
                    Messages = [.. conversation.Messages, .. turn.Messages],
                    Tools = definitions,
                    ToolChoice = lastTrip ? new ToolChoiceNone() : new ToolChoiceAuto(),
                    Thinking = new ThinkingConfigAdaptive(),
                    OutputConfig = new OutputConfig { Effort = Enum.Parse<Effort>(settings.Effort, ignoreCase: true) },
                    CacheControl = new CacheControlEphemeral(),
                };
                var response = listener is null
                    ? await anthropic.Messages.Create(request, cancellationToken)
                    : await StreamAsync(request, listener, trip, cancellationToken);
                turn.Count(response);

                var (assistant, toolUses) = ProtonEcho.Of(response);
                turn.Messages.Add(new MessageParam { Role = Role.Assistant, Content = assistant });

                if (response.StopReason == StopReason.ToolUse && toolUses.Count > 0 && !lastTrip)
                {
                    turn.Messages.Add(new MessageParam
                    {
                        Role = Role.User,
                        Content = await RunToolsAsync(tools, toolUses, turn, conversation.Focus, listener, cancellationToken),
                    });
                    continue;
                }

                turn.Finish(response, Answer(response), lastTrip);
                if (turn.Outcome == ProtonOutcome.Refused)
                {
                    // the category only (a policy name), never the question
                    _log.LogInformation("Proton's model declined a question ({Category}).", response.StopDetails?.Category?.Raw() ?? "no category");
                }

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
        ProtonToolSession tools, IReadOnlyList<ToolUseBlock> toolUses, ProtonTurn turn, ConversationFocus focus, IProtonListener? listener,
        CancellationToken cancellationToken)
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
                if (listener is not null)
                {
                    await listener.StatusAsync(ProtonStatus.Of(toolUse.Name), cancellationToken);
                }

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

    /// <summary>
    /// One model call, streamed: the answer's text goes to the listener as it is written, and the reply is put back
    /// together for the tool loop. The stream may run as long as the whole question may.
    /// </summary>
    private async Task<Message> StreamAsync(MessageCreateParams request, IProtonListener listener, int trip, CancellationToken cancellationToken)
    {
        var client = anthropic.WithOptions(o => o with { Timeout = TimeSpan.FromSeconds(options.Value.StreamQuestionTimeoutSeconds) });
        var message = new ProtonStreamedMessage();
        await foreach (var streamEvent in client.Messages.CreateStreaming(request, cancellationToken))
        {
            message.Add(streamEvent.Json);
            if (streamEvent.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
            {
                await listener.DeltaAsync(trip, text.Text, cancellationToken);
            }
        }

        return message.ToMessage();
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
    /// Facts the model needs each question, written by the app (not the user): today in the company's time zone, who
    /// is asking and what they can see, and what the conversation is about. Sent with the question rather than in the
    /// system prompt, so the prompt stays the same for everyone and is cached.
    /// </summary>
    private string ContextNote(ClaimsPrincipal caller, UserAccess access, ProtonConversation conversation, ProtonNotes notes)
    {
        var name = $"{caller.FindFirst(AuthClaimTypes.FirstName)?.Value} {caller.FindFirst(AuthClaimTypes.LastName)?.Value}".Trim();
        var today = clock.CompanyToday;
        var actions = conversation.TakeActionNotes();
        var note = $"""
            [Context from the app — not written by the user]
            Today: {today.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)} (company time zone).
            Asking: {(name.Length > 0 ? name : "a user")}, {access.Role.Replace('_', ' ')}, who can see: {string.Join(", ", access.VisibleDomains)}.
            Conversation focus: {conversation.Focus.Describe()}.
            """;
        if (notes.Company is { Length: > 0 } company)
        {
            note += $"\nCompany: {company}.";
        }

        if (notes.Page is { } page)
        {
            note += $"\nWhere the user is in the app (where they are, not data): {page}.";
        }

        if (conversation.Recap is { } recap)
        {
            note += $"\n{recap}";
        }

        if (notes.Suggested)
        {
            note += "\nThis message is one of the replies you suggested, which the user tapped rather than typed.";
        }

        return actions.Count == 0 ? note : note + "\nSince the last message (from the confirmation buttons): " + string.Join(" ", actions);
    }

    /// <summary>What a conversation's answers depended on: if any of it changes, the conversation ends.</summary>
    private static string Fingerprint(UserAccess access)
        => string.Join('|', access.Role, access.Domain, string.Join(',', access.VisibleDomains), string.Join(',', access.Permissions.Order(StringComparer.Ordinal)));
}

/// <summary>What the app adds to a question's context note besides the conversation's own state.</summary>
/// <param name="Page">Where the user is, already checked against what they may see.</param>
/// <param name="Company">The company's name from its branding, when set.</param>
/// <param name="Suggested">The question is one of Proton's suggested replies, tapped rather than typed.</param>
internal sealed record ProtonNotes(string? Page, string? Company, bool Suggested = false);

/// <summary>One question's progress: the messages it adds, the tools it ran, the tokens it used and how it ended.</summary>
internal sealed class ProtonTurn(string model)
{
    private const int MaxAnswerLength = 8_000;

    private readonly Stopwatch _watch = Stopwatch.StartNew();

    private ProtonTokens _tokens = ProtonTokens.None;

    public List<MessageParam> Messages { get; } = [];

    public List<ProtonToolResult> ToolCalls { get; } = [];

    public int RoundTrips { get; private set; }

    /// <summary>Milliseconds from the question to the first model reply; null if none came.</summary>
    public long? FirstReplyMs { get; private set; }

    public long ElapsedMs => _watch.ElapsedMilliseconds;

    /// <summary>Input tokens of the last model call, cached ones included.</summary>
    public int LastInputTokens { get; private set; }

    public ProtonOutcome Outcome { get; private set; } = ProtonOutcome.Failed;

    public string Answer { get; private set; } = string.Empty;

    public void Count(Message response)
    {
        RoundTrips++;
        FirstReplyMs ??= _watch.ElapsedMilliseconds;
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
        Tools = JsonSerializer.Serialize(ToolCalls.Select(t => new { tool = t.Tool, input = ProtonInputSummary.Summarize(t.Input), status = t.Status.ToString(), ms = t.ElapsedMs }), ProtonJson.Options),
    };
}

/// <summary>
/// A question that passed every check (<see cref="ProtonChatAgent.PrepareAsync"/>). It holds its conversation, one
/// question at a time, until it is disposed.
/// </summary>
public sealed class ProtonQuestion : IAsyncDisposable
{
    internal ProtonQuestion(
        ClaimsPrincipal caller, long userId, UserAccess access, ProtonConversation conversation, ProtonToolSession tools,
        AskProtonRequest request, string text, ProtonNotes notes, bool rolledOver)
    {
        (Caller, UserId, Access, Conversation, Tools, Request, Text, Notes, RolledOver) =
            (caller, userId, access, conversation, tools, request, text, notes, rolledOver);
    }

    internal ClaimsPrincipal Caller { get; }

    internal long UserId { get; }

    internal UserAccess Access { get; }

    internal ProtonConversation Conversation { get; }

    internal ProtonToolSession Tools { get; }

    internal AskProtonRequest Request { get; }

    /// <summary>The question as the model gets it: typed, or written by the app for a button.</summary>
    internal string Text { get; }

    internal ProtonNotes Notes { get; }

    internal bool RolledOver { get; }

    public ValueTask DisposeAsync()
    {
        Conversation.End();
        return ValueTask.CompletedTask;
    }
}
