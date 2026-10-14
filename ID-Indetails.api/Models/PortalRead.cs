namespace ID_Indetails.api.Models;

/// <summary>How far one person (a staff member or a client) has read a project's chat and comments. Only moves forward.</summary>
public class PortalRead
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long? UserId { get; set; }

    public long? AccountId { get; set; }

    public long? LastMessageId { get; set; }

    public long? LastCommentId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
