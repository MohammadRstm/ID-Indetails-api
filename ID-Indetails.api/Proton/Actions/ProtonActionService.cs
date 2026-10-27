using System.Security.Claims;
using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Configuration.Options;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>
/// The user's answer to a proposed action. Confirming runs it at most once, and only when it is the user's own pending
/// action in that conversation, not expired, still allowed (the write endpoint's policies are evaluated again now), and
/// still valid. The existing service then applies its own checks and does the write.
/// </summary>
public sealed class ProtonActionService(
    IProtonActionRepository actions,
    ProtonToolbox toolbox,
    ProtonConversationStore conversations,
    IPermissionService permissions,
    IClock clock,
    IOptions<ProtonOptions> options,
    ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger(ProtonToolbox.LogCategory);

    public async Task<ProtonActionOutcomeResponse> ConfirmAsync(
        ClaimsPrincipal caller, long userId, Guid actionId, DecideProtonActionRequest request, CancellationToken cancellationToken)
    {
        var action = await PendingOfAsync(userId, actionId, request.ConversationId, cancellationToken);
        var now = clock.UtcNow;
        if (action.ExpiresAt <= now)
        {
            await actions.CloseAsync(actionId, ProtonActionStatus.Expired, now, cancellationToken);
            throw new BusinessRuleException(ErrorCodes.ProtonActionExpired, "This proposal has expired. Ask me again if you still want it.");
        }

        var settings = options.Value.Actions;
        if (ProtonCoverage.FamilyOf(action.Tool) is not { } family || !settings.IsOn(family))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionsOff, "This kind of change is switched off for Proton now. You can make it in the app.");
        }

        if (await actions.CountConfirmedSinceAsync(userId, now.AddHours(-1), cancellationToken) >= settings.MaxConfirmedActionsPerHour)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionLimit,
                $"You've confirmed {settings.MaxConfirmedActionsPerHour} of my changes in the last hour. Try again a bit later, or make it in the app.");
        }

        // the typed phrase is the one the server stored with the proposal, never one the client sends along
        if (Preview(action) is { Confirm: ProtonConfirm.Typed, Phrase: { } phrase }
            && !string.Equals(request.Typed?.Trim(), phrase.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonPhraseMismatch, $"Type “{phrase}” exactly to confirm.");
        }

        // proposed under other permissions: the card was made for access the user no longer has
        var call = CallOf(action);
        if (call is null || call.Fingerprint != ProtonToolbox.FingerprintOf(await permissions.GetAccessAsync(userId, cancellationToken)))
        {
            await actions.CloseAsync(actionId, ProtonActionStatus.Superseded, now, cancellationToken);
            throw new ConflictException(ErrorCodes.ProtonActionNotPending, "Your access changed since I proposed this. Ask me again.");
        }

        // From here on it can never run twice: only one request moves it out of pending.
        if (!await actions.TryClaimAsync(actionId, userId, now, cancellationToken))
        {
            throw NotPending(await actions.FindAsync(actionId, cancellationToken));
        }

        ProtonActionDone done;
        try
        {
            // claimed, it runs to the end: a closed browser or a dropped connection must not stop it half way
            var session = await toolbox.OpenAsync(caller, userId,
                new ProtonOrigin(action.ConversationId, action.Model, action.PromptVersion), CancellationToken.None);
            done = await session.ExecuteAsync(action.Id, action.Tool, call.Args.GetRawText(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            var failure = FailureOf(exception);
            await actions.FinishAsync(actionId, ProtonActionStatus.Failed, null, null, failure.Code, Short(failure.Message), clock.UtcNow, CancellationToken.None);
            Note(userId, action, failure.MaybeSaved
                ? $"The user confirmed \"{Title(action)}\" but something went wrong while it was being made: it may or may not have been " +
                  "saved. Say so, and before proposing it again, check with a tool whether it is there."
                : $"The user confirmed \"{Title(action)}\" but it failed, and nothing was changed: {failure.Message}");
            _log.Log(failure.MaybeSaved ? LogLevel.Error : LogLevel.Warning, exception,
                "Proton action {ActionId} ({Tool}) failed with {Code}.", actionId, action.Tool, failure.Code);
            if (failure.MaybeSaved)
            {
                // the client is told only what is safe to know: that it may have been saved; the cause is in the log
                throw new ConflictException(failure.Code, failure.Message);
            }

            throw;
        }

        await actions.FinishAsync(actionId, ProtonActionStatus.Succeeded, done.ResultType, done.ResultId, null, null, clock.UtcNow, CancellationToken.None);
        Note(userId, action, $"The user confirmed \"{Title(action)}\" and it was done: {done.Message}");
        _log.LogInformation("Proton action {ActionId} ({Tool}) succeeded on {ResultType} {ResultId}.", actionId, action.Tool, done.ResultType, done.ResultId);
        // Navigate only when the card the user confirmed said what would open.
        IReadOnlyList<ProtonUiCommand> opens = Preview(action)?.OpensAfter is not null && done.Opens is { } command ? [command] : [];
        return new ProtonActionOutcomeResponse(actionId, ProtonActionStatus.Succeeded, done.Message, done.Refs, opens);
    }

    public async Task<ProtonActionOutcomeResponse> CancelAsync(
        long userId, Guid actionId, DecideProtonActionRequest request, CancellationToken cancellationToken)
    {
        var action = await PendingOfAsync(userId, actionId, request.ConversationId, cancellationToken);
        if (!await actions.CloseAsync(actionId, ProtonActionStatus.Cancelled, clock.UtcNow, cancellationToken))
        {
            throw NotPending(await actions.FindAsync(actionId, cancellationToken));
        }

        Note(userId, action, $"The user cancelled \"{Title(action)}\". Nothing was changed.");
        return new ProtonActionOutcomeResponse(actionId, ProtonActionStatus.Cancelled, "Cancelled. Nothing was changed.", [], []);
    }

    /// <param name="MaybeSaved">The change may have been written before it failed.</param>
    public sealed record Failure(string Code, string Message, bool MaybeSaved);

    /// <summary>
    /// What a failed confirmation tells the user and the model. A refusal (a business rule, a validation) comes before the
    /// service writes anything; any other error may come after its save, so it is never reported as "nothing changed".
    /// </summary>
    public static Failure FailureOf(Exception exception) => exception switch
    {
        AppException app => new Failure(app.Code, app.Message, false),
        ValidationException validation => new Failure(
            ErrorCodes.ValidationFailed, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()), false),
        _ => new Failure(ErrorCodes.ProtonActionUncertain, "Something went wrong while this was being made, and it may have been saved. Check before asking again.", true),
    };

    private const int Listed = 50;

    /// <summary>The caller's own proposals, newest first: restored cards and the panel read their status here.</summary>
    public async Task<IReadOnlyList<ProtonActionSummaryResponse>> MineAsync(long userId, CancellationToken cancellationToken)
        => (await actions.MineAsync(userId, Listed, cancellationToken)).Select(a => Summary(a, null)).ToList();

    /// <summary>Everyone's proposals, for those who manage Proton: what it proposed, to whom, and what was confirmed.</summary>
    public async Task<IReadOnlyList<ProtonActionSummaryResponse>> AllAsync(CancellationToken cancellationToken)
        => (await actions.AllAsync(Listed, cancellationToken)).Select(r => Summary(r.Action, r.Who)).ToList();

    private ProtonActionSummaryResponse Summary(ProtonAction a, string? who)
    {
        // a pending proposal past its time reads as expired, as a confirmation would find it
        var status = a.Status == ProtonActionStatus.Pending && a.ExpiresAt <= clock.UtcNow ? ProtonActionStatus.Expired : a.Status;
        return new ProtonActionSummaryResponse(a.PublicId, a.Tool, Title(a), status, a.CreatedAt, a.DecidedAt, a.ResultType, a.ResultId, a.ErrorMessage, who);
    }

    /// <summary>The caller's own action in that conversation, still pending. Anyone else's reads as not found.</summary>
    private async Task<ProtonAction> PendingOfAsync(long userId, Guid actionId, Guid conversationId, CancellationToken cancellationToken)
    {
        var action = await actions.FindAsync(actionId, cancellationToken);
        if (action is null || action.UserId != userId || action.ConversationId != conversationId)
        {
            throw new NotFoundException(ErrorCodes.ProtonActionNotFound, "Proposal not found.");
        }

        return action.Status == ProtonActionStatus.Pending ? action : throw NotPending(action);
    }

    private static ConflictException NotPending(ProtonAction? action) => new(ErrorCodes.ProtonActionNotPending, action?.Status switch
    {
        ProtonActionStatus.Executing => "This proposal is already being carried out.",
        ProtonActionStatus.Succeeded => "This proposal was already carried out.",
        ProtonActionStatus.Failed => "This proposal was already tried and failed. Ask me again if you want another go.",
        ProtonActionStatus.Cancelled => "This proposal was cancelled.",
        ProtonActionStatus.Superseded => "A newer proposal replaced this one.",
        _ => "This proposal has expired. Ask me again if you still want it.",
    });

    private void Note(long userId, ProtonAction action, string note)
    {
        var conversation = conversations.Find(userId, action.ConversationId);
        conversation?.NoteAction(note);
        conversation?.Decided(action.PublicId);
    }

    private static ProtonCall? CallOf(ProtonAction action)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ProtonCall>(action.Arguments, ProtonJson.Options) is { Fingerprint: { Length: > 0 } } call ? call : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The card a proposal showed, as stored with it.</summary>
    public static ProtonPreview? Preview(ProtonAction action)
        => System.Text.Json.JsonSerializer.Deserialize<ProtonPreview>(action.Preview, ProtonJson.Options);

    private static string Title(ProtonAction action) => Preview(action)?.Title ?? action.Tool;

    private static string Short(string message) => message.Length <= 500 ? message : message[..500];
}
