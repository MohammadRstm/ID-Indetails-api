namespace ID_Indetails.api.Models;

/// <summary>One of the company's offices: a point on the website's globe, and an address and map on its contact page.</summary>
public class WebsiteLocation : IWebsiteListItem
{
    public long Id { get; set; }

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public string Address { get; set; } = string.Empty;

    /// <summary>Free text in international format.</summary>
    public string Phone { get; set; } = string.Empty;

    public int Position { get; set; }
}
