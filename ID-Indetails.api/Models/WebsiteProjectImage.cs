namespace ID_Indetails.api.Models;

/// <summary>One picture of a website project, in order: in its gallery, or one of its up to five concept pictures.</summary>
public class WebsiteProjectImage
{
    public long Id { get; set; }

    public long WebsiteProjectId { get; set; }

    public long FileId { get; set; }

    public string Alt { get; set; } = string.Empty;

    public int Position { get; set; }

    /// <summary>A concept picture (the project page's Concept row) rather than a gallery one; each is ordered on its own.</summary>
    public bool Concept { get; set; }
}
