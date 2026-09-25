namespace ID_Indetails.api.Models;

/// <summary>
/// A notification for one user. Stores structured data only; the client builds the text and
/// the link from <see cref="Kind"/> and the subject, so web and mobile can both render it.
/// </summary>
public class Notification
{
    public long Id { get; set; }

    public long RecipientId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public long? ActorId { get; set; }

    public long? ProjectId { get; set; }

    public string? SubjectType { get; set; }

    public long? SubjectId { get; set; }

    public string? Payload { get; set; }

    /// <summary>Set for rolling digests (e.g. one "work logged today" notification per day).</summary>
    public string? DigestKey { get; set; }

    public int Count { get; set; } = 1;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public User Recipient { get; set; } = null!;
}
