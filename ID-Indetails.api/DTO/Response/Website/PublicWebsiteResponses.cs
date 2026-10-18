namespace ID_Indetails.api.DTO.Response.Website;

// What the public website reads (docs/website.md). The shapes are the website's own; picture addresses are paths on
// this API (/api/public/website/files/{id}).

public sealed record PublicImageCredit(string Photographer, string Source, string Url);

public sealed record PublicImage(string Src, string Alt, PublicImageCredit? Credit);

public sealed record PublicPlace(string City, string Country, decimal Lat, decimal Lng);

/// <param name="Type">"villa", "residential" or "hotel".</param>
/// <param name="Status">"in-progress" or "completed".</param>
/// <param name="Client">Empty when the website names no client.</param>
/// <param name="Thumbnail">The portrait picture's address.</param>
public sealed record PublicWebsiteProject(
    long Id,
    string Slug,
    string Title,
    string Type,
    string Status,
    string Client,
    PublicPlace Location,
    int Year,
    string Description,
    string Thumbnail,
    PublicImage Hero,
    IReadOnlyList<PublicImage> Gallery,
    bool ShowInConveyor,
    int? ConveyorOrder);

public sealed record PublicWebsiteOffering(long Id, string Title, string Description);

public sealed record PublicWebsiteLocation(long Id, string City, string Country, decimal Lat, decimal Lng, string Address, string Phone);

/// <param name="Email">Null until the company sets one.</param>
public sealed record PublicWebsiteContact(string? Email);

public sealed record PublicWebsiteSocialLink(long Id, string Label, string Url);

/// <param name="FaviconUrl">Null until a favicon is chosen.</param>
public sealed record PublicWebsiteSettings(string? FaviconUrl);
