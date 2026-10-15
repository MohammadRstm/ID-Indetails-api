namespace ID_Indetails.api.Models;

/// <summary>
/// A message in a project's client portal chat, written by a staff member or a client (exactly one of the two
/// authors). Its author can edit it; nobody deletes it.
/// </summary>
public class PortalMessage
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long? AuthorUserId { get; set; }

    public long? AuthorAccountId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Made by the browser for each message, so sending it again gives the same message.</summary>
    public string ClientToken { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }
}
