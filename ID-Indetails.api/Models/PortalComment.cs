namespace ID_Indetails.api.Models;

/// <summary>
/// A comment on one published file of a project's client portal, by a staff member or a client. It keeps the item
/// and file it was written about (plain ids, not foreign keys: the item can be unpublished and the file replaced and
/// deleted later), with their names at the time, so the history stays readable and never moves to another file.
/// Its author can edit or delete it; a deleted comment keeps its place without its text.
/// </summary>
public class PortalComment
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long ItemId { get; set; }

    /// <summary>The published file the comment was written about.</summary>
    public long FileId { get; set; }

    public string ItemTitle { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long? AuthorUserId { get; set; }

    public long? AuthorAccountId { get; set; }

    /// <summary>Null once deleted.</summary>
    public string? Body { get; set; }

    public string ClientToken { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
