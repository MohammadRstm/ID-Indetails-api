using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Conversation;

/// <summary>
/// Conversations in server memory, keyed by their owner and id: one person can never reach another's chat. They
/// expire after a while idle and are lost on restart, which is fine for Stage 1 (the transcript is in proton_usage).
/// </summary>
public sealed class ProtonConversationStore(IMemoryCache cache, IOptions<ProtonOptions> options)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(4);

    public ProtonConversation? Find(long userId, Guid conversationId)
        => cache.TryGetValue(Key(userId, conversationId), out ProtonConversation? conversation) ? conversation : null;

    public ProtonConversation Start(long userId, string accessFingerprint)
    {
        var conversation = new ProtonConversation(Guid.NewGuid(), userId, accessFingerprint);
        cache.Set(Key(userId, conversation.Id), conversation, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(options.Value.ConversationIdleMinutes),
            AbsoluteExpirationRelativeToNow = MaxAge,
        });
        return conversation;
    }

    public void Remove(long userId, Guid conversationId) => cache.Remove(Key(userId, conversationId));

    /// <summary>
    /// The person's conversation to carry on, or a new one when they name none. One that is gone, or that began under
    /// different permissions, is ended rather than continued.
    /// </summary>
    public ProtonConversation Open(long userId, Guid? conversationId, string accessFingerprint)
    {
        if (conversationId is not { } id)
        {
            return Start(userId, accessFingerprint);
        }

        var conversation = Find(userId, id)
            ?? throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "I no longer have our earlier chat, so we're starting fresh.");
        if (conversation.AccessFingerprint != accessFingerprint)
        {
            // Earlier answers were given under different permissions: they must not carry over, not even as a recap.
            Remove(userId, id);
            throw new NotFoundException(ErrorCodes.ProtonConversationEnded, "Your access changed, so I closed our earlier chat and we're starting fresh.");
        }

        return conversation;
    }

    private static string Key(long userId, Guid conversationId) => $"proton:conversation:{userId}:{conversationId:N}";
}
