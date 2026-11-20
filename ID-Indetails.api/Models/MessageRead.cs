namespace ID_Indetails.api.Models;

/// <summary>
/// When someone read an announcement, for the "Seen" list of whoever announces. Kept for Announcements only: in the
/// other chats, the read markers (<see cref="ConversationRead"/>) say who has seen a message, without the time.
/// </summary>
public class MessageRead
{
    public long MessageId { get; set; }

    public long UserId { get; set; }

    public DateTime ReadAt { get; set; }

    public Message Message { get; set; } = null!;

    public User User { get; set; } = null!;
}
