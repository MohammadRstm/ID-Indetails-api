using System.Text.Json;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Proton.History;

/// <summary>A question Proton answered, to save: what the user saw of both.</summary>
/// <param name="Question">The question as the user saw it: what they typed or said, or the button they pressed.</param>
public sealed record ProtonExchange(
    long UserId,
    ProtonConversation Conversation,
    UserAccess Access,
    string Question,
    ProtonMessageOrigin Origin,
    ProtonReplyResponse Reply);

/// <summary>
/// People's saved chats: each answered question is kept with its answer, so its owner can read the chat again and carry
/// it on after the server has let it go. Only the owner ever reaches a chat, and a chat held under access the owner no
/// longer has is not shown. Never the tool results behind an answer: only what the user saw.
/// </summary>
public sealed class ProtonHistoryService(
    IProtonHistoryRepository history,
    IProtonActionRepository actions,
    ProtonConversationStore conversations,
    IPermissionService permissions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Messages a carried-on chat starts from: its last three questions and answers.</summary>
    private const int ResumeMessages = 6;

    private const int SweepBatch = 200;

    public const string HiddenText = "This chat was held with access you no longer have, so it isn't shown.";

    /// <summary>
    /// The conversation a question goes to: a new one, the one the server still holds, or a saved one carried on from
    /// its last messages. One held under different access is not carried on.
    /// </summary>
    public async Task<ProtonConversation> OpenAsync(long userId, Guid? conversationId, string accessFingerprint, CancellationToken cancellationToken)
    {
        if (conversationId is not { } id)
        {
            return conversations.Start(userId, accessFingerprint);
        }

        if (conversations.OpenHeld(userId, id, accessFingerprint) is { } held)
        {
            return held;
        }

        var saved = await history.FindAsync(userId, id, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "I no longer have our earlier chat, so we're starting fresh.");
        if (saved.AccessFingerprint != ProtonHistoryRules.AccessKey(accessFingerprint))
        {
            throw ProtonConversationStore.AccessChanged();
        }

        var last = await history.LastMessagesAsync(id, ResumeMessages, cancellationToken);
        return conversations.Restore(userId, id, accessFingerprint, Exchanges(last));
    }

    /// <summary>Saves an answered question and its answer, starting the saved chat with its first one.</summary>
    public async Task RecordAsync(ProtonExchange exchange, CancellationToken cancellationToken)
    {
        var (userId, conversation, access, question, origin, reply) = exchange;
        var now = clock.UtcNow;
        var saved = await history.FindAsync(userId, conversation.Id, cancellationToken);
        if (saved is null)
        {
            saved = new ProtonStoredConversation
            {
                Id = conversation.Id,
                UserId = userId,
                Title = ProtonHistoryRules.TitleOf(question),
                CreatedAt = now,
                UpdatedAt = now,
                AccessFingerprint = ProtonHistoryRules.AccessKey(conversation.AccessFingerprint),
                AccessPermissions = JsonSerializer.Serialize(access.Permissions.Order(StringComparer.Ordinal).ToList(), ProtonJson.Options),
                AccessDomains = JsonSerializer.Serialize(access.VisibleDomains.Order(StringComparer.Ordinal).ToList(), ProtonJson.Options),
            };
            history.Add(saved);
        }

        history.Add(new ProtonStoredMessage
        {
            ConversationId = saved.Id,
            Seq = saved.MessageCount + 1,
            Role = ProtonMessageRole.User,
            Origin = origin,
            Text = question,
            CreatedAt = now,
        });
        history.Add(new ProtonStoredMessage
        {
            ConversationId = saved.Id,
            Seq = saved.MessageCount + 2,
            Role = ProtonMessageRole.Proton,
            Text = reply.Reply,
            Refs = reply.Refs.Count == 0 ? null : JsonSerializer.Serialize(reply.Refs, ProtonJson.Options),
            Sources = reply.Sources.Count == 0 ? null : JsonSerializer.Serialize(reply.Sources, ProtonJson.Options),
            ActionIds = reply.PendingActions.Count == 0 ? null : JsonSerializer.Serialize(reply.PendingActions.Select(a => a.Id).ToList(), ProtonJson.Options),
            Blocks = reply.Blocks.Count == 0 ? null : JsonSerializer.Serialize(reply.Blocks, ProtonJson.Options),
            CreatedAt = now,
        });
        saved.MessageCount += 2;
        saved.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<ProtonConversationSummaryResponse>> ListAsync(
        long userId, ProtonConversationListQuery query, CancellationToken cancellationToken)
    {
        var (items, total) = await history.ListAsync(userId, query.Search, query.Page, query.PageSize, cancellationToken);
        return new PagedResponse<ProtonConversationSummaryResponse>(
            items.Select(Summary).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<ProtonConversationResponse> GetAsync(long userId, Guid id, CancellationToken cancellationToken)
    {
        var saved = await FindAsync(userId, id, cancellationToken);
        var access = await permissions.GetAccessAsync(userId, cancellationToken);
        if (ProtonHistoryRules.LostAccess(Codes(saved.AccessPermissions), Codes(saved.AccessDomains), access))
        {
            return new ProtonConversationResponse(saved.Id, saved.Title, saved.CreatedAt, saved.UpdatedAt, false, HiddenText, []);
        }

        var cards = (await actions.InConversationAsync(userId, id, cancellationToken)).ToDictionary(a => a.PublicId);
        var messages = (await history.MessagesAsync(id, cancellationToken))
            .Select(m => new ProtonStoredMessageResponse(
                m.Seq,
                m.Role,
                m.Origin,
                m.Text,
                Read<List<ProtonRef>>(m.Refs) ?? [],
                Read<List<ProtonSourceResponse>>(m.Sources) ?? [],
                (Read<List<Guid>>(m.ActionIds) ?? []).Select(a => cards.GetValueOrDefault(a)).OfType<ProtonAction>().Select(Card).OfType<ProtonStoredCardResponse>().ToList(),
                Read<List<Ui.ProtonBlock>>(m.Blocks) ?? [],
                m.CreatedAt))
            .ToList();
        return new ProtonConversationResponse(
            saved.Id, saved.Title, saved.CreatedAt, saved.UpdatedAt, saved.AccessFingerprint == ProtonHistoryRules.AccessKey(ProtonToolbox.FingerprintOf(access)), null, messages);
    }

    public async Task<ProtonConversationSummaryResponse> RenameAsync(long userId, Guid id, RenameProtonConversationRequest request, CancellationToken cancellationToken)
    {
        var saved = await FindAsync(userId, id, cancellationToken);
        saved.Title = string.Join(' ', request.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Summary(saved);
    }

    /// <summary>
    /// Deletes the person's chat: its messages, the server's hold on it, and its words in the usage ledger (the cost
    /// stays, so the budget still adds up). Deleting one that is already gone is fine.
    /// </summary>
    public async Task DeleteAsync(long userId, Guid id, CancellationToken cancellationToken)
    {
        conversations.Remove(userId, id);
        if (await history.DeleteAsync(userId, id, cancellationToken))
        {
            await history.ClearUsageTextAsync([id], cancellationToken);
        }
    }

    /// <summary>Deletes every chat nobody has touched since <paramref name="before"/>, with its words in the usage ledger.</summary>
    public async Task<int> DeleteUntouchedSinceAsync(DateTime before, CancellationToken cancellationToken)
    {
        var deleted = 0;
        for (var ids = await history.UntouchedSinceAsync(before, SweepBatch, cancellationToken);
             ids.Count > 0;
             ids = await history.UntouchedSinceAsync(before, SweepBatch, cancellationToken))
        {
            await history.ClearUsageTextAsync(ids, cancellationToken);
            deleted += await history.DeleteManyAsync(ids, cancellationToken);
        }

        return deleted;
    }

    private async Task<ProtonStoredConversation> FindAsync(long userId, Guid id, CancellationToken cancellationToken)
        => await history.FindAsync(userId, id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, "Chat not found.");

    private static ProtonConversationSummaryResponse Summary(ProtonStoredConversation c)
        => new(c.Id, c.Title, c.CreatedAt, c.UpdatedAt, c.MessageCount);

    /// <summary>The saved questions and answers in pairs, oldest first, for a carried-on chat's recap.</summary>
    private static IEnumerable<(string Question, string Answer)> Exchanges(IReadOnlyList<ProtonStoredMessage> messages)
    {
        for (var i = 0; i + 1 < messages.Count; i++)
        {
            if (messages[i].Role == ProtonMessageRole.User && messages[i + 1].Role == ProtonMessageRole.Proton)
            {
                yield return (messages[i].Text, messages[i + 1].Text);
                i++;
            }
        }
    }

    private ProtonStoredCardResponse? Card(ProtonAction action)
    {
        if (ProtonActionService.Preview(action) is not { } preview)
        {
            return null;
        }

        // a pending proposal past its time reads as expired, as a confirmation would find it
        var status = action.Status == ProtonActionStatus.Pending && action.ExpiresAt <= clock.UtcNow ? ProtonActionStatus.Expired : action.Status;
        return new ProtonStoredCardResponse(
            new ProtonPendingAction(action.PublicId, action.Tool, action.Risk, preview, action.ExpiresAt), status, action.ErrorMessage);
    }

    private static IReadOnlyList<string> Codes(string json) => Read<List<string>>(json) ?? [];

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, ProtonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
