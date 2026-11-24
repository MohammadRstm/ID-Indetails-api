using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A direct-message thread between two users, a team's group chat, the single Announcements chat, or a custom chat
/// group. A team chat's membership is not stored: it is the team's members plus the domain's admins plus super admins.
/// Every active person reads Announcements, and holders of <c>message:announce</c> write in it. A custom group has a
/// name and the members a holder of <c>message:manage_groups</c> picked (<see cref="Members"/>).
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

    /// <summary>Custom groups only: the group's name.</summary>
    public string? Name { get; set; }

    /// <summary>Custom groups only: the group's photo, shown instead of the people icon.</summary>
    public long? PhotoFileId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Team? Team { get; set; }

    /// <summary>Custom groups only.</summary>
    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
