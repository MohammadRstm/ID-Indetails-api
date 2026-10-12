using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Tools;

public enum ProtonToolStatus
{
    Ok,
    Invalid,
    NotFound,
    Forbidden,
    Refused,
    Timeout,
    Failed,
}

/// <summary>One tool call: what the model gets back, and what is logged and kept in the transcript.</summary>
public sealed record ProtonToolResult(
    string Tool,
    JsonElement Input,
    ProtonToolStatus Status,
    string Content,
    IReadOnlyList<ProtonRef> Refs,
    DayRange? Period,
    int ElapsedMs)
{
    /// <summary>Set when an action tool proposed a change: it now waits for the user's confirmation.</summary>
    public ProtonPendingAction? Pending { get; init; }

    public ProtonBasis? Basis { get; init; }

    public Ui.ProtonUiCommand? UiCommand { get; init; }

    public IReadOnlyList<Ui.ProtonChoice>? Choices { get; init; }

    public IReadOnlyList<Ui.ProtonQuickReplyOption>? QuickReplies { get; init; }

    public IReadOnlyList<string>? Suggestions { get; init; }

    public Ui.ProtonBlock? Block { get; init; }
}

/// <summary>What the model is told of a dataset a tool returned: enough to show it, none of its values (they are in data).</summary>
/// <param name="Forms">The forms it fits as it is.</param>
/// <param name="Notes">What the answer should say with it: a period that isn't over, what was left out.</param>
public sealed record ProtonDatasetNote(
    string Id, string Title, string? Period, int Rows, int TotalRows, IReadOnlyList<string> Columns, IReadOnlyList<Ui.ProtonPresentForm> Forms, IReadOnlyList<string>? Notes);

/// <summary>Which conversation, model and prompt the tools are used for: recorded with every proposed action.</summary>
public sealed record ProtonOrigin(Guid ConversationId, string Model, string PromptVersion);

/// <summary>
/// The shared tool layer every Proton interface uses. It offers a caller only the tools whose endpoints they may call,
/// and runs each call read-only, inside the existing services and their authorization, with a time limit. An action
/// tool's proposal is stored here as a pending action; it runs only through <see cref="ProtonToolSession.ExecuteAsync"/>,
/// which only the user's confirmation reaches.
/// </summary>
public sealed class ProtonToolbox(
    IEnumerable<IProtonTool> tools,
    IAuthorizationService authorization,
    IPermissionService permissions,
    ICompanySettingsService companySettings,
    IProtonActionRepository actions,
    IUnitOfWork unitOfWork,
    IClock clock,
    IServiceProvider services,
    IOptions<ProtonOptions> options,
    ILoggerFactory loggers)
{
    public const string LogCategory = "ID_Indetails.api.Proton";

    private const int MaxContentLength = 40_000;

    private readonly ILogger _log = loggers.CreateLogger(LogCategory);

    public async Task<ProtonToolSession> OpenAsync(ClaimsPrincipal caller, long userId, ProtonOrigin origin, CancellationToken cancellationToken)
    {
        var settings = await companySettings.GetAsync(cancellationToken);
        var access = await permissions.GetAccessAsync(userId, cancellationToken);
        var context = new ProtonToolContext(
            caller,
            access,
            authorization,
            services,
            clock.CompanyToday,
            settings.WeekStart == 0 ? DayOfWeek.Sunday : DayOfWeek.Monday);

        var available = new List<IProtonTool>();
        foreach (var tool in tools)
        {
            if ((tool.OnlyInDomain is null || access.CanSee(tool.OnlyInDomain)) && await context.CanCallAsync(tool.Endpoint))
            {
                available.Add(tool);
            }
        }

        return new ProtonToolSession(this, context, access, origin, available);
    }

    internal async Task<ProtonToolResult> RunAsync(ProtonToolSession session, string name, JsonElement input, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var tool = session.Tools.FirstOrDefault(t => t.Name == name);
        ProtonToolResult result;
        if (tool is null || !await session.Context.CanCallAsync(tool.Endpoint))
        {
            result = Fail(name, input, ProtonToolStatus.Forbidden, "This tool isn't available to this user.", watch);
        }
        else if (tool is IProtonActionTool && session.Context.Turn != ProtonTurnOrigin.Typed && ProtonCoverage.NeedsTypedTurn(name))
        {
            // A click or a suggested reply can't start a send, a delete, a money or an access change: text in the data
            // could have steered the option, so the user asks for it in their own words. Nor can speech: a transcript
            // can mishear an amount, a name or an address.
            result = Fail(name, input, ProtonToolStatus.Refused, session.Context.Turn == ProtonTurnOrigin.Spoken
                ? "This kind of change can't be asked for by voice. Ask the user to type what they want in the chat."
                : "This kind of change needs the user to ask for it in their own words. Ask them to type what they want.", watch);
        }
        else if (tool is IProtonActionTool && (ProtonCoverage.FamilyOf(name) is not { } family || !options.Value.Actions.IsOn(family)))
        {
            // the tool stays on the list (so the prompt cache holds) and says so
            result = Fail(name, input, ProtonToolStatus.Refused,
                "This kind of change isn't switched on for Proton yet: the user does it in the app. Offer to open the place for them.", watch);
        }
        else if (tool.Heavy && session.HeavyCalls >= options.Value.MaxHeavyToolCallsPerQuestion)
        {
            result = Fail(name, input, ProtonToolStatus.Refused,
                $"This question has had its {options.Value.MaxHeavyToolCallsPerQuestion} larger analyses. Answer with what you have, and say what you couldn't check.", watch);
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ToolTimeoutSeconds));
            if (tool.Heavy)
            {
                session.HeavyCalls++;
            }

            try
            {
                // Action tools only propose here: they run inside the same read-only guard as every read.
                var output = await RunGuardedAsync(tool, input, session.Context, timeout.Token);
                // the result's datasets are held for present_data; the model gets their ids and shapes, not their values twice
                var held = output.Datasets?.Select(d => session.Context.HoldDataset(name, d, clock.UtcNow)).ToList();
                var notes = held?.Select(h => new ProtonDatasetNote(
                    h.Id, h.Dataset.Title, h.Dataset.Period, h.Dataset.Rows.Count, h.Dataset.Total,
                    h.Dataset.Columns.Select(c => c.Unit is { } unit ? $"{c.Key} ({unit.ToString().ToLowerInvariant()})" : c.Key).ToList(),
                    Ui.ProtonPresentationRules.FormsFor(h.Dataset),
                    h.Dataset.Notes is { Count: > 0 } said ? said : null)).ToList();
                result = output.Proposal is { } proposal && tool is IProtonActionTool action
                    ? await ProposeAsync(session, action, input, proposal, output, watch, cancellationToken)
                    : Ok(name, input, new { status = "ok", basis = output.Basis, datasets = notes, data = output.Data }, output, watch);
                if (result.Status == ProtonToolStatus.Ok)
                {
                    session.Context.Remember(output.Refs);
                }
                else
                {
                    session.Context.Drop(held ?? []);
                }
            }
            catch (ProtonToolInputException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Invalid, exception.Message, watch);
            }
            catch (ValidationException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Invalid, string.Join(" ", exception.Errors.Select(e => e.ErrorMessage).Distinct()), watch);
            }
            catch (NotFoundException)
            {
                // Missing and not visible read the same, as in the API: nothing about other domains leaks.
                result = Fail(name, input, ProtonToolStatus.NotFound, "Not found, or not visible to this user.", watch);
            }
            catch (ForbiddenException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Forbidden, exception.Message, watch);
            }
            catch (AppException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Refused, exception.Message, watch);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                result = Fail(name, input, ProtonToolStatus.Timeout, "This took too long. Try a narrower question.", watch);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _log.LogError(exception, "Proton tool {Tool} failed.", name);
                result = Fail(name, input, ProtonToolStatus.Failed, "The data couldn't be read right now.", watch);
            }
        }

        var level = result.Status is ProtonToolStatus.Forbidden ? LogLevel.Warning : LogLevel.Information;
        _log.Log(level, "Proton tool {Tool} {Status} in {ElapsedMs} ms with {Input}.",
            name, result.Status, result.ElapsedMs, ProtonInputSummary.Of(input));
        return result;
    }

    /// <summary>
    /// The tool inside the read-only guard. A change it staged without saving would be written by the next save in the
    /// request (the proposal row, the usage record), so it is dropped and the call refused, as a database write is.
    /// </summary>
    private async Task<ProtonToolOutput> RunGuardedAsync(IProtonTool tool, JsonElement input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var stagedBefore = unitOfWork.HasChanges;
        var leaked = false;
        ProtonToolOutput? output = null;
        try
        {
            output = await ProtonReadOnly.RunAsync(() => tool.RunAsync(input, context, cancellationToken));
        }
        finally
        {
            if (!stagedBefore && unitOfWork.HasChanges)
            {
                unitOfWork.DiscardChanges();
                leaked = true;
            }
        }

        return leaked ? throw new ProtonWriteRefusedException("Proton tools are read-only: a change staged during a lookup was dropped.") : output!;
    }

    /// <summary>
    /// Stores a proposal as a pending action, up to the cap per conversation. An earlier card for the same tool and
    /// target is superseded, so a changed request always needs a new confirmation; other cards stay. Tells the model
    /// nothing has changed yet.
    /// </summary>
    private async Task<ProtonToolResult> ProposeAsync(
        ProtonToolSession session, IProtonActionTool tool, JsonElement input, ProtonProposal proposal, ProtonToolOutput output,
        Stopwatch watch, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var targetType = proposal.Target?.Type.ToString().ToLowerInvariant();
        if (await actions.CountPendingAsync(session.Origin.ConversationId, tool.Name, targetType, proposal.Target?.Id, now, cancellationToken) >= options.Value.Actions.MaxPendingActions)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonTooManyPending,
                $"{options.Value.Actions.MaxPendingActions} changes are already waiting for the user's Confirm: ask them to confirm or cancel one first.");
        }

        var pending = new ProtonAction
        {
            PublicId = Guid.NewGuid(),
            UserId = session.Access.UserId,
            Role = session.Access.Role,
            DomainCode = session.Access.Domain,
            ConversationId = session.Origin.ConversationId,
            Tool = tool.Name,
            Risk = tool.Risk,
            Arguments = JsonSerializer.Serialize(new ProtonCall(FingerprintOf(session.Access), JsonSerializer.Deserialize<JsonElement>(proposal.Arguments)), ProtonJson.Options),
            Preview = JsonSerializer.Serialize(proposal.Preview, ProtonJson.Options),
            TargetType = targetType,
            TargetId = proposal.Target?.Id,
            Status = ProtonActionStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(options.Value.ActionMinutes),
            Model = session.Origin.Model,
            PromptVersion = session.Origin.PromptVersion,
        };
        actions.Add(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await actions.SupersedeAsync(pending.ConversationId, pending.Tool, pending.TargetType, pending.TargetId, pending.PublicId, now, cancellationToken);
        session.Context.ActionPending = true;
        _log.LogInformation("Proton proposed {Tool} as action {ActionId}.", tool.Name, pending.PublicId);

        var model = new
        {
            status = "proposed",
            action_id = pending.PublicId,
            proposal = proposal.Preview,
            expires_at = pending.ExpiresAt,
            note = "Nothing has changed. The user now sees a card with exactly this and must press Confirm on it. " +
                   "Tell them what will happen and that it needs their confirmation; never say it is done.",
        };
        return Ok(tool.Name, input, model, output, watch) with
        {
            Pending = new ProtonPendingAction(pending.PublicId, tool.Name, tool.Risk, proposal.Preview, pending.ExpiresAt, proposal.Target),
        };
    }

    /// <summary>
    /// Runs a confirmed action: only if the user still passes the write endpoint's policies now, and outside the
    /// read-only guard. The tool re-validates its stored request and calls the existing service.
    /// </summary>
    internal async Task<ProtonActionDone> ExecuteAsync(
        ProtonToolSession session, long actionId, string name, string arguments, CancellationToken cancellationToken)
    {
        var tool = session.Tools.OfType<IProtonActionTool>().FirstOrDefault(t => t.Name == name);
        if (tool is null || !await session.Context.CanCallAsync(tool.Endpoint))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You're no longer allowed to do this.");
        }

        session.Context.ConfirmedActionId = actionId;
        return await tool.ExecuteAsync(arguments, session.Context, cancellationToken);
    }

    /// <summary>What answers and proposals depend on: if any of it changes, a conversation ends and its cards can't be confirmed.</summary>
    public static string FingerprintOf(UserAccess access)
        => string.Join('|', access.Role, access.Domain, string.Join(',', access.VisibleDomains), string.Join(',', access.Permissions.Order(StringComparer.Ordinal)));

    private static ProtonToolResult Ok(string tool, JsonElement input, object content, ProtonToolOutput output, Stopwatch watch)
    {
        var json = JsonSerializer.Serialize(content, ProtonJson.Options);
        return json.Length > MaxContentLength
            ? Fail(tool, input, ProtonToolStatus.Invalid, "The result is too large. Ask for fewer rows or a shorter period.", watch)
            : new ProtonToolResult(tool, input, ProtonToolStatus.Ok, json, output.Refs, output.Period, Elapsed(watch))
            {
                Basis = output.Basis, UiCommand = output.UiCommand, Choices = output.Choices, QuickReplies = output.QuickReplies, Suggestions = output.Suggestions,
                Block = output.Block,
            };
    }

    private static ProtonToolResult Fail(string tool, JsonElement input, ProtonToolStatus status, string message, Stopwatch watch)
        => new(tool, input, status,
            JsonSerializer.Serialize(new { status, message }, ProtonJson.Options),
            [], null, Elapsed(watch));

    private static int Elapsed(Stopwatch watch) => (int)watch.ElapsedMilliseconds;
}

/// <summary>The tools one caller may use, for one question or one confirmation.</summary>
public sealed class ProtonToolSession(
    ProtonToolbox toolbox, ProtonToolContext context, UserAccess access, ProtonOrigin origin, IReadOnlyList<IProtonTool> tools)
{
    public IReadOnlyList<IProtonTool> Tools => tools;

    internal ProtonToolContext Context => context;

    internal UserAccess Access => access;

    internal ProtonOrigin Origin => origin;

    /// <summary>Heavy tool calls so far: a session serves one question.</summary>
    internal int HeavyCalls { get; set; }

    public Task<ProtonToolResult> RunAsync(string name, JsonElement input, CancellationToken cancellationToken)
        => toolbox.RunAsync(this, name, input, cancellationToken);

    /// <summary>For the confirmation flow only (<see cref="ProtonActionService"/>); no model ever reaches it.</summary>
    internal Task<ProtonActionDone> ExecuteAsync(long actionId, string name, string arguments, CancellationToken cancellationToken)
        => toolbox.ExecuteAsync(this, actionId, name, arguments, cancellationToken);
}

/// <summary>
/// Tool inputs for logs and the usage record: ids, numbers, dates and enum values as they are; other text only as its
/// length, so a question's names, notes and messages are never kept through its tool calls.
/// </summary>
public static class ProtonInputSummary
{
    public static string Of(JsonElement input) => Summarize(input).ToJsonString();

    public static JsonObject Summarize(JsonElement input)
    {
        var summary = new JsonObject();
        if (input.ValueKind != JsonValueKind.Object)
        {
            return summary;
        }

        foreach (var property in input.EnumerateObject())
        {
            summary[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String when IsCode(property.Value.GetString()!) => property.Value.GetString(),
                JsonValueKind.String => $"text({property.Value.GetString()!.Length})",
                JsonValueKind.Array => $"list({property.Value.GetArrayLength()})",
                JsonValueKind.Object => "object",
                _ => JsonNode.Parse(property.Value.GetRawText()),
            };
        }

        return summary;
    }

    private static bool IsCode(string value)
        => value.Length <= 32 && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-');
}
