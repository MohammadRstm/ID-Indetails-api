namespace ID_Indetails.api.Models;

/// <summary>A row of one of the website's ordered lists (services, locations, social links).</summary>
public interface IWebsiteListItem
{
    long Id { get; }

    int Position { get; set; }
}
