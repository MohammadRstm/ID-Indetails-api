using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Website;

public sealed record WebsiteImageCreditResponse(string Photographer, string Source, string Url);

/// <summary>A gallery picture; <paramref name="FileId"/> opens it through <c>GET /api/files/{id}</c>.</summary>
public sealed record WebsiteImageResponse(long Id, long FileId, string Alt, WebsiteImageCreditResponse? Credit);

/// <summary>The system project a website project shows, with the status and year the website takes from it.</summary>
public sealed record WebsiteLinkedProjectResponse(long Id, string Title, string DomainCode, WebsiteProjectStatus Status, int Year);

/// <param name="Status">Its own status; null while it follows the linked project's.</param>
/// <param name="Year">Its own year; null while it follows the linked project's.</param>
/// <param name="ShownStatus">What the website shows.</param>
/// <param name="ShownYear">What the website shows.</param>
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
    decimal? Latitude,
    decimal? Longitude,
    string? Description,
    long? ThumbnailFileId,
    long? HeroFileId,
    string? HeroAlt,
    WebsiteImageCreditResponse? HeroCredit,
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

public sealed record WebsiteLocationResponse(long Id, string City, string Country, decimal Latitude, decimal Longitude, string Address, string Phone);

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
    bool CanManage);
