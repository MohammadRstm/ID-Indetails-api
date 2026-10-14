namespace ID_Indetails.api.Models;

/// <summary>Someone (a staff member or a client) mentioned in a chat message or a comment.</summary>
public class PortalMention
{
    public long Id { get; set; }

    public long? MessageId { get; set; }

    public long? CommentId { get; set; }

    public long? UserId { get; set; }

    public long? AccountId { get; set; }
}
