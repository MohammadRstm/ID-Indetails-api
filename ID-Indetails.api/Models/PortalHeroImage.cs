namespace ID_Indetails.api.Models;

/// <summary>
/// One of a project's client portal hero images, in order. The draft list is staff's (<see cref="IsPublished"/> false);
/// publishing makes the published list (true) a copy of it, and discarding the other way round. A row keeps its file
/// whatever happens to the item it was taken from.
/// </summary>
public class PortalHeroImage
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long FileId { get; set; }

    /// <summary>Ascending within the list; ties go by id.</summary>
    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    public PortalFile File { get; set; } = null!;
}
