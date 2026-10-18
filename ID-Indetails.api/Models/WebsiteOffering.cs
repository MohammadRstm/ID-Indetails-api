namespace ID_Indetails.api.Models;

/// <summary>One of the services the company offers, as the website lists them (stored in website_services).</summary>
public class WebsiteOffering : IWebsiteListItem
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Plain text.</summary>
    public string Description { get; set; } = string.Empty;

    public int Position { get; set; }
}
