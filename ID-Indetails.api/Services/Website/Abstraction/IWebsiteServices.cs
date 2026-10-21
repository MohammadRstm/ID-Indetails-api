using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Services.Files.Abstraction;

namespace ID_Indetails.api.Services.Website.Abstraction;

/// <summary>The website's projects, as the Website page edits them (docs/website.md).</summary>
public interface IWebsiteProjectService
{
    Task<WebsiteProjectsResponse> ListAsync(CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> CreateAsync(WebsiteProjectRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> UpdateAsync(long id, WebsiteProjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>Its pictures are attached to nothing any more, so the file cleanup deletes them.</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The whole moving strip, in order.</summary>
    Task<WebsiteProjectsResponse> SetConveyorAsync(WebsiteConveyorRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> AddImageAsync(long id, WebsiteImageRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> UpdateImageAsync(long id, long imageId, WebsiteImageEditRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> RemoveImageAsync(long id, long imageId, CancellationToken cancellationToken = default);

    Task<WebsiteProjectResponse> OrderImagesAsync(long id, WebsiteOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>System projects to link, from both businesses.</summary>
    Task<IReadOnlyList<WebsiteLinkCandidateResponse>> CandidatesAsync(string? search, CancellationToken cancellationToken = default);
}

/// <summary>The rest of the Website page: services, locations, social links and the favicon. Every change answers with all of it.</summary>
public interface IWebsiteContentService
{
    Task<WebsiteContentResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> AddServiceAsync(WebsiteServiceRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> UpdateServiceAsync(long id, WebsiteServiceRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> DeleteServiceAsync(long id, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> OrderServicesAsync(WebsiteOrderRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> AddLocationAsync(WebsiteLocationRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> UpdateLocationAsync(long id, WebsiteLocationRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> DeleteLocationAsync(long id, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> OrderLocationsAsync(WebsiteOrderRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> AddSocialLinkAsync(WebsiteSocialLinkRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> UpdateSocialLinkAsync(long id, WebsiteSocialLinkRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> DeleteSocialLinkAsync(long id, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> OrderSocialLinksAsync(WebsiteOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>The countries a place is chosen from.</summary>
    IReadOnlyList<WebsiteCountryResponse> Countries();

    /// <summary>Whether a picture (found anywhere) carries the company's invisible watermark, and which website picture it is.</summary>
    Task<WatermarkCheckResponse> CheckWatermarkAsync(Stream picture, CancellationToken cancellationToken = default);

    /// <summary>The country's cities (by English name or code), biggest first, narrowed by what was typed. 404 for an unknown country.</summary>
    IReadOnlyList<WebsiteCityResponse> Cities(string country, string? search);

    Task<WebsiteContentResponse> SetFaviconAsync(AttachFileRequest request, CancellationToken cancellationToken = default);

    Task<WebsiteContentResponse> RemoveFaviconAsync(CancellationToken cancellationToken = default);

    /// <summary>Puts the website under construction (kept from the first time) or brings it back.</summary>
    Task<WebsiteContentResponse> SetUnderConstructionAsync(WebsiteUnderConstructionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What the public website reads, with no sign-in, and its contact form.</summary>
public interface IPublicWebsiteService
{
    Task<IReadOnlyList<PublicWebsiteProject>> ProjectsAsync(CancellationToken cancellationToken = default);

    Task<PublicWebsiteProject> ProjectAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicWebsiteOffering>> ServicesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicWebsiteLocation>> LocationsAsync(CancellationToken cancellationToken = default);

    Task<PublicWebsiteContact> ContactAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicWebsiteSocialLink>> SocialLinksAsync(CancellationToken cancellationToken = default);

    Task<PublicWebsiteSettings> SettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>A picture the website shows; anything else reads as not found.</summary>
    /// <summary>A picture the website shows; with <paramref name="size"/>, its smaller copy (<see cref="IPictureSizes"/>).</summary>
    Task<FileContent> OpenFileAsync(long fileId, int? size = null, CancellationToken cancellationToken = default);

    /// <summary>Emails the message to the company's own address. A robot's message is dropped without a word.</summary>
    Task SendContactMessageAsync(WebsiteContactMessageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The Website area's analytics, from Google Analytics (docs/website.md, "Analytics").</summary>
public interface IWebsiteAnalyticsService
{
    /// <summary>
    /// The period's reports, compared with the period before unless asked not to. 503
    /// <c>website_analytics_not_configured</c> while Google Analytics isn't connected; 503
    /// <c>website_analytics_unavailable</c> when Google fails.
    /// </summary>
    Task<WebsiteAnalyticsResponse> ReportAsync(WebsiteAnalyticsQuery query, CancellationToken cancellationToken = default);

    /// <summary>The last 30 minutes, at most a minute old.</summary>
    Task<WebsiteAnalyticsRealtimeResponse> RealtimeAsync(CancellationToken cancellationToken = default);
}
