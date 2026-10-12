using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Conversation;

/// <summary>
/// Conversations in server memory, keyed by their owner and id: one person can never reach another's chat. They
/// expire after a while idle and are lost on restart; a saved chat is then carried on from its history
/// (ProtonHistoryService).
/// </summary>
public sealed class ProtonConversationStore(IMemoryCache cache, IOptions<ProtonOptions> options)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(4);

    public ProtonConversation? Find(long userId, Guid conversationId)
        => cache.TryGetValue(Key(userId, conversationId), out ProtonConversation? conversation) ? conversation : null;

    public ProtonConversation Start(long userId, string accessFingerprint) => Hold(new ProtonConversation(Guid.NewGuid(), userId, accessFingerprint));

    /// <summary>A saved chat the server no longer held, carried on under its id from its last questions and answers.</summary>
    public ProtonConversation Restore(long userId, Guid conversationId, string accessFingerprint, IEnumerable<(string Question, string Answer)> exchanges)
    {
        var conversation = new ProtonConversation(conversationId, userId, accessFingerprint);
        conversation.Resume(exchanges);
        return Hold(conversation);
    }

    public void Remove(long userId, Guid conversationId) => cache.Remove(Key(userId, conversationId));

    /// <summary>
    /// The person's conversation held in memory, to carry on; null when it isn't held (it may be saved). One that began
    /// under different permissions is ended rather than continued.
    /// </summary>
    public ProtonConversation? OpenHeld(long userId, Guid conversationId, string accessFingerprint)
    {
        var conversation = Find(userId, conversationId);
        if (conversation is not null && conversation.AccessFingerprint != accessFingerprint)
        {
            // Earlier answers were given under different permissions: they must not carry over, not even as a recap.
            Remove(userId, conversationId);
            throw AccessChanged();
        }

        return conversation;
    }

    public static NotFoundException AccessChanged()
        => new(ErrorCodes.ProtonConversationEnded, "Your access changed since that chat, so I can't carry it on. Let's start a new one.");

    private ProtonConversation Hold(ProtonConversation conversation)
    {
        cache.Set(Key(conversation.UserId, conversation.Id), conversation, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(options.Value.ConversationIdleMinutes),
            AbsoluteExpirationRelativeToNow = MaxAge,
        });
        return conversation;
    }

    private static string Key(long userId, Guid conversationId) => $"proton:conversation:{userId}:{conversationId:N}";
}
