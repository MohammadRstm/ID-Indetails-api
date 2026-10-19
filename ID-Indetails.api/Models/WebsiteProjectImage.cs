namespace ID_Indetails.api.Models;

/// <summary>One picture in a website project's gallery, in order.</summary>
public class WebsiteProjectImage
{
    public long Id { get; set; }

    public long WebsiteProjectId { get; set; }

    public long FileId { get; set; }

    public string Alt { get; set; } = string.Empty;

    public int Position { get; set; }
}
