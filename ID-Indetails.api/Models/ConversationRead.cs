namespace ID_Indetails.api.Models;

/// <summary>
/// How far a user has read a conversation. Anything newer than
/// <see cref="LastReadMessageId"/> and not sent by them is unread. <see cref="LastDeliveredMessageId"/> is how far
/// their devices have received it (the second grey tick); reading counts as receiving too.
/// </summary>
public class ConversationRead
{
    public long UserId { get; set; }

    public long ConversationId { get; set; }

    public long? LastReadMessageId { get; set; }

    public long? LastDeliveredMessageId { get; set; }

    /// <summary>When the read marker last moved.</summary>
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Conversation Conversation { get; set; } = null!;
}
