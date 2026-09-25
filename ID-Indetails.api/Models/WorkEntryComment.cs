namespace ID_Indetails.api.Models;

public class WorkEntryComment
{
    public long Id { get; set; }

    public long WorkEntryId { get; set; }

    public long AuthorId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Whether the comment was also posted to the author's team group chat.</summary>
    public bool SentToGroup { get; set; }

    /// <summary>The group-chat message created when <see cref="SentToGroup"/> is true.</summary>
    public long? MessageId { get; set; }

    public DateTime CreatedAt { get; set; }

    public WorkEntry WorkEntry { get; set; } = null!;

    public User Author { get; set; } = null!;
}
