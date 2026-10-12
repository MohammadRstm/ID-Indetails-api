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
using ID_Indetails.api.Services.Common.Abstraction;

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
    IClock clock,
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
            throw new BusinessRuleException(ErrorCodes.ProtonActionExpired, "This proposal has expired. Ask Proton again.");
        }

        // From here on it can never run twice: only one request moves it out of pending.
        if (!await actions.TryClaimAsync(actionId, userId, now, cancellationToken))
        {
            throw NotPending(await actions.FindAsync(actionId, cancellationToken));
        }

        ProtonActionDone done;
        try
        {
            var session = await toolbox.OpenAsync(caller, userId,
                new ProtonOrigin(action.ConversationId, action.Model, action.PromptVersion), cancellationToken);
            done = await session.ExecuteAsync(action.Tool, action.Arguments, cancellationToken);
        }
        catch (Exception exception)
        {
            var (code, message) = exception switch
            {
                AppException app => (app.Code, app.Message),
                ValidationException validation => (ErrorCodes.ValidationFailed, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct())),
                _ => (ErrorCodes.InternalError, "Something went wrong."),
            };
            await actions.FinishAsync(actionId, ProtonActionStatus.Failed, null, null, code, Short(message), clock.UtcNow, CancellationToken.None);
            Note(userId, action, $"The user confirmed \"{Title(action)}\" but it failed: {message}");
            _log.Log(exception is AppException or ValidationException ? LogLevel.Warning : LogLevel.Error, exception,
                "Proton action {ActionId} ({Tool}) failed with {Code}.", actionId, action.Tool, code);
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
        ProtonActionStatus.Failed => "This proposal was already tried and failed. Ask Proton again.",
        ProtonActionStatus.Cancelled => "This proposal was cancelled.",
        ProtonActionStatus.Superseded => "A newer proposal replaced this one.",
        _ => "This proposal has expired. Ask Proton again.",
    });

    private void Note(long userId, ProtonAction action, string note)
        => conversations.Find(userId, action.ConversationId)?.NoteAction(note);

    private static ProtonPreview? Preview(ProtonAction action)
        => System.Text.Json.JsonSerializer.Deserialize<ProtonPreview>(action.Preview, ProtonJson.Options);

    private static string Title(ProtonAction action) => Preview(action)?.Title ?? action.Tool;

    private static string Short(string message) => message.Length <= 500 ? message : message[..500];
}
