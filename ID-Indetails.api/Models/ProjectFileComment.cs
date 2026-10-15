namespace ID_Indetails.api.Models;

/// <summary>
/// A staff comment on one of a project's files (the item, whatever file it shows now). Plain text; its author can edit
/// or delete it, and the file's uploader or a project files manager marks it fixed. Deleting a never-published item
/// deletes its comments with it.
/// </summary>
public class ProjectFileComment
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long ItemId { get; set; }

    public long AuthorId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    /// <summary>Null while the comment is open.</summary>
    public DateTime? FixedAt { get; set; }

    public long? FixedById { get; set; }
}
