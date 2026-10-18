namespace ID_Indetails.api.Models;

/// <summary>A link in the website's footer, such as "Instagram".</summary>
public class WebsiteSocialLink : IWebsiteListItem
{
    public long Id { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>Always https.</summary>
    public string Url { get; set; } = string.Empty;

    public int Position { get; set; }
}
