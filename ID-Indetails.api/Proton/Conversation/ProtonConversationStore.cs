using ID_Indetails.api.Configuration.Options;
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

    private static string Key(long userId, Guid conversationId) => $"proton:conversation:{userId}:{conversationId:N}";
}
