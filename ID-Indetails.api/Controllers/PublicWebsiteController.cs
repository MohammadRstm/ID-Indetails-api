using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Website.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// What the company's public website reads: no sign-in, read-only apart from the contact form, and only what the website
/// shows. Visitors get their own roomier rate limit (RateLimiting:PublicWebsitePermitsPerMinute); answers may be cached
/// for a minute, pictures for a year. See docs/website.md.
/// </summary>
[ApiController]
[Route(PublicWebsitePaths.Prefix)]
[AllowAnonymous]
public sealed class PublicWebsiteController(IPublicWebsiteService website) : ControllerBase
{
    private const int ContentSeconds = 60;

    /// <summary>
    /// A picture's address never changes its content: a new picture is a new file, and a copy's size is in its address.
    /// So a browser keeps it a year and doesn't ask again.
    /// </summary>
    private const string PictureCaching = "public, max-age=31536000, immutable";

    /// <summary>Every published project: the globe, the moving strip (showInConveyor, in conveyorOrder) and the grid.</summary>
    [HttpGet("projects")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IReadOnlyList<PublicWebsiteProject>> Projects(CancellationToken cancellationToken) => website.ProjectsAsync(cancellationToken);

    [HttpGet("projects/{slug}")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<PublicWebsiteProject> Project(string slug, CancellationToken cancellationToken) => website.ProjectAsync(slug, cancellationToken);

    [HttpGet("services")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IReadOnlyList<PublicWebsiteOffering>> Services(CancellationToken cancellationToken) => website.ServicesAsync(cancellationToken);

    [HttpGet("locations")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IReadOnlyList<PublicWebsiteLocation>> Locations(CancellationToken cancellationToken) => website.LocationsAsync(cancellationToken);

    [HttpGet("contact")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<PublicWebsiteContact> Contact(CancellationToken cancellationToken) => website.ContactAsync(cancellationToken);

    [HttpGet("social-links")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<IReadOnlyList<PublicWebsiteSocialLink>> SocialLinks(CancellationToken cancellationToken) => website.SocialLinksAsync(cancellationToken);

    [HttpGet("settings")]
    [ResponseCache(Duration = ContentSeconds, Location = ResponseCacheLocation.Any)]
    public Task<PublicWebsiteSettings> Settings(CancellationToken cancellationToken) => website.SettingsAsync(cancellationToken);

    /// <summary>
    /// A picture the website shows, cached for a year (<see cref="PictureCaching"/>); a picture that isn't shown is a 404
    /// that isn't cached. <c>?size=</c> asks for a smaller copy (its longest side, rounded up to 160, 480, 960 or
    /// 1920 px): a card or a thumbnail needn't download the whole photo.
    /// </summary>
    [HttpGet("files/{id:long}")]
    public async Task<IActionResult> Picture(long id, [FromQuery] int? size, CancellationToken cancellationToken)
    {
        var file = await website.OpenFileAsync(id, size, cancellationToken);
        Response.Headers.CacheControl = PictureCaching;
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.MimeType);
    }

    /// <summary>The contact form: emails the company. 202 once sent; nothing is echoed back.</summary>
    [HttpPost("contact-messages")]
    [EnableRateLimiting(RateLimitPolicies.WebsiteContact)]
    public async Task<IActionResult> SendContactMessage(WebsiteContactMessageRequest request, CancellationToken cancellationToken)
    {
        await website.SendContactMessageAsync(request, cancellationToken);
        return Accepted();
    }
}
