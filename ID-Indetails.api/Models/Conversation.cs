using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A direct-message thread between two users, a team's group chat, or the single Announcements chat. Group membership
/// is not stored: it is the team's members plus the domain's admins plus super admins. Every active person reads
/// Announcements, and holders of <c>message:announce</c> write in it.
/// </summary>
public class Conversation
{
    public long Id { get; set; }

    public ConversationType Type { get; set; }

    /// <summary>The team's domain for group chats. Null for direct messages.</summary>
    public string? DomainCode { get; set; }

    /// <summary>Set for group chats only.</summary>
    public long? TeamId { get; set; }

    /// <summary>Direct messages only: the lower of the two user ids.</summary>
    public long? DmUserLowId { get; set; }

    /// <summary>Direct messages only: the higher of the two user ids.</summary>
    public long? DmUserHighId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Team? Team { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
