namespace ID_Indetails.api.Models;

/// <summary>
/// A project's client portal as a whole: its hero image and introduction (draft and published), the revision every
/// draft change moves on, and the last publication. The row is created the first time anything changes, and locked by
/// every change so they happen one at a time.
/// </summary>
public class PortalSettings
{
    public long ProjectId { get; set; }

    /// <summary>The draft hero image. A file of its own or one an item shows; it keeps the file whatever happens to the item.</summary>
    public long? HeroFileId { get; set; }

    public long? PublishedHeroFileId { get; set; }

    /// <summary>The draft introduction for the client: plain text.</summary>
    public string? Summary { get; set; }

    public string? PublishedSummary { get; set; }

    /// <summary>Moves on with every draft change; publishing and discarding name the revision the person saw.</summary>
    public int Revision { get; set; }

    public int? PublishedRevision { get; set; }

    /// <summary>Null until the first publication.</summary>
    public DateTime? PublishedAt { get; set; }

    public long? PublishedById { get; set; }
}
