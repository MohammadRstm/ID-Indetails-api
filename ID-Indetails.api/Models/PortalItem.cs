using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One entry in a section of a project's client portal: its file, title and place in the order. Everything is a draft
/// until publishing exists; replacing the file keeps the item, its title and its place.
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

    public PortalFile File { get; set; } = null!;
}
