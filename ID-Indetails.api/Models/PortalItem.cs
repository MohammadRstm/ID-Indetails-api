using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One entry in a section of a project's client portal. The plain columns are the draft staff work on; the
/// <c>Published*</c> columns are what clients see, copied from the draft when the project is published. Replacing the
/// file keeps the item, its title and its place.
/// </summary>
public class PortalItem
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public PortalSection Section { get; set; }

    public long FileId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Ascending within the section, with gaps of 1024 so an item can later move between two others; ties go by id.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long UpdatedById { get; set; }

    /// <summary>The file clients see; null until the item is first published.</summary>
    public long? PublishedFileId { get; set; }

    public string? PublishedTitle { get; set; }

    public PortalSection? PublishedSection { get; set; }

    public int? PublishedSortOrder { get; set; }

    /// <summary>Deleted in the draft; clients still see the published copy until the next publish.</summary>
    public DateTime? RemovedAt { get; set; }

    public PortalFile File { get; set; } = null!;
}
