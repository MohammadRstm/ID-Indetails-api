using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Website;

/// <summary>A gallery picture; <paramref name="FileId"/> opens it through <c>GET /api/files/{id}</c>.</summary>
public sealed record WebsiteImageResponse(long Id, long FileId, string Alt);

/// <summary>The system project a website project shows, with the status and year the website takes from it.</summary>
public sealed record WebsiteLinkedProjectResponse(long Id, string Title, string DomainCode, WebsiteProjectStatus Status, int Year);

/// <param name="Status">Its own status; null while it follows the linked project's.</param>
/// <param name="Year">Its own year; null while it follows the linked project's.</param>
/// <param name="ShownStatus">What the website shows.</param>
/// <param name="ShownYear">What the website shows.</param>
/// <param name="PositionApproximate">The point was picked near the city or a town of the country, as no coordinates were given.</param>
/// <param name="MissingToPublish">What it still needs before it can be published; empty when it can be.</param>
public sealed record WebsiteProjectResponse(
    long Id,
    string Slug,
    string Title,
    WebsiteProjectType Type,
    WebsiteProjectStatus? Status,
    int? Year,
    WebsiteProjectStatus? ShownStatus,
    int? ShownYear,
    string? Client,
    string? City,
    string? Country,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude,
    bool PositionApproximate,
    string? Description,
    long? ThumbnailFileId,
    long? HeroFileId,
    string? HeroAlt,
    WebsiteLinkedProjectResponse? LinkedProject,
    bool Published,
    int? ConveyorOrder,
    IReadOnlyList<WebsiteImageResponse> Gallery,
    IReadOnlyList<string> MissingToPublish,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <param name="ConveyorLimit">How many projects the moving strip shows at most.</param>
/// <param name="CanManage">Whether the caller may change the website (<c>website:manage</c>).</param>
public sealed record WebsiteProjectsResponse(IReadOnlyList<WebsiteProjectResponse> Projects, int ConveyorLimit, bool CanManage);

/// <summary>A system project that can be shown on the website, with what the form fills in from it.</summary>
/// <param name="WebsiteProjectId">The website project already showing it, if any: a project is shown once.</param>
public sealed record WebsiteLinkCandidateResponse(
    long Id,
    string Title,
    string DomainCode,
    WebsiteProjectStatus Status,
    int Year,
    string? Client,
    string? Description,
    long? WebsiteProjectId);

public sealed record WebsiteServiceResponse(long Id, string Title, string Description);

/// <param name="PositionApproximate">The point is a random one in the city, as only the city and country were given.</param>
public sealed record WebsiteLocationResponse(
    long Id, string City, string Country, string? CountryCode, decimal Latitude, decimal Longitude, bool PositionApproximate, string Address, string Phone);

/// <summary>A country of the place lists: its ISO 3166-1 code and English name.</summary>
public sealed record WebsiteCountryResponse(string Code, string Name);

/// <summary>A city of the place lists, with its point and how many people live there.</summary>
public sealed record WebsiteCityResponse(string Name, decimal Latitude, decimal Longitude, int Population);

/// <summary>A city of another country than the one chosen: where to look instead.</summary>
public sealed record WebsiteCityElsewhereResponse(string Name, string CountryCode, string CountryName);

public sealed record WebsiteSocialLinkResponse(long Id, string Label, string Url);

/// <summary>Everything on the Website page apart from the projects.</summary>
/// <param name="ContactEmail">The company's own address: shown on the website, and where its contact form sends.</param>
/// <param name="FaviconFileId">Opens through <c>GET /api/files/{id}</c>.</param>
public sealed record WebsiteContentResponse(
    IReadOnlyList<WebsiteServiceResponse> Services,
    IReadOnlyList<WebsiteLocationResponse> Locations,
    IReadOnlyList<WebsiteSocialLinkResponse> SocialLinks,
    string? ContactEmail,
    long? FaviconFileId,
    DateTime? UnderConstructionSince,
    bool CanManage);
