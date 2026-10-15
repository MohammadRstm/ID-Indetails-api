namespace ID_Indetails.api.Models;

/// <summary>How far one person (a staff member or a client) has read one file's comments. Only moves forward.</summary>
public class PortalCommentRead
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>The thread's item (a plain id, as on the comments: the item may be unpublished later).</summary>
    public long ItemId { get; set; }

    public long? UserId { get; set; }

    public long? AccountId { get; set; }

    public long LastCommentId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
