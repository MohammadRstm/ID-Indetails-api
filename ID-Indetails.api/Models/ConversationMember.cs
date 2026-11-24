namespace ID_Indetails.api.Models;

/// <summary>Someone in a custom chat group. Only custom groups store their members; team chats work theirs out live.</summary>
public class ConversationMember
{
    public long ConversationId { get; set; }

    public long UserId { get; set; }

    public DateTime AddedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public User User { get; set; } = null!;
}
