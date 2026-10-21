using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Services.Website.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The Website page: what the company's public website shows. Reading needs <c>website:view</c> or
/// <c>website:manage</c>; changing needs <c>website:manage</c>. Pictures are uploaded with <c>POST /api/files</c> first.
/// The contact email is <c>PUT /api/company/contact-email</c>. See docs/website.md.
/// </summary>
[ApiController]
[Route("api/website")]
public sealed class WebsiteController(IWebsiteProjectService projects, IWebsiteContentService content) : ControllerBase
{
    /// <summary>Every website project, published or not, newest first.</summary>
    [HttpGet("projects")]
    [HasAnyPermission(PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectsResponse> Projects(CancellationToken cancellationToken) => projects.ListAsync(cancellationToken);

    [HttpGet("projects/{id:long}")]
    [HasAnyPermission(PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectResponse> Project(long id, CancellationToken cancellationToken) => projects.GetAsync(id, cancellationToken);

    [HttpPost("projects")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public async Task<ActionResult<WebsiteProjectResponse>> CreateProject(WebsiteProjectRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await projects.CreateAsync(request, cancellationToken));

    [HttpPut("projects/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectResponse> UpdateProject(long id, WebsiteProjectRequest request, CancellationToken cancellationToken)
        => projects.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("projects/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public async Task<IActionResult> DeleteProject(long id, CancellationToken cancellationToken)
    {
        await projects.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>The website's moving strip: up to 20 published projects, in order.</summary>
    [HttpPut("conveyor")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectsResponse> SetConveyor(WebsiteConveyorRequest request, CancellationToken cancellationToken)
        => projects.SetConveyorAsync(request, cancellationToken);

    [HttpPost("projects/{id:long}/images")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectResponse> AddImage(long id, WebsiteImageRequest request, CancellationToken cancellationToken)
        => projects.AddImageAsync(id, request, cancellationToken);

    [HttpPut("projects/{id:long}/images/{imageId:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectResponse> UpdateImage(long id, long imageId, WebsiteImageEditRequest request, CancellationToken cancellationToken)
        => projects.UpdateImageAsync(id, imageId, request, cancellationToken);

    [HttpDelete("projects/{id:long}/images/{imageId:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectResponse> RemoveImage(long id, long imageId, CancellationToken cancellationToken)
        => projects.RemoveImageAsync(id, imageId, cancellationToken);

    /// <summary>The gallery's new order: every one of its pictures, once. 409 when it changed meanwhile.</summary>
    [HttpPut("projects/{id:long}/images/order")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteProjectResponse> OrderImages(long id, WebsiteOrderRequest request, CancellationToken cancellationToken)
        => projects.OrderImagesAsync(id, request, cancellationToken);

    /// <summary>System projects of the businesses the caller can see to link a website project to, with what the form fills in.</summary>
    [HttpGet("link-candidates")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<IReadOnlyList<WebsiteLinkCandidateResponse>> LinkCandidates([FromQuery] string? search, CancellationToken cancellationToken)
        => projects.CandidatesAsync(search, cancellationToken);

    /// <summary>
    /// Whether a picture found anywhere (a download, a screenshot) carries the company's invisible watermark, and which
    /// website picture it is: the multipart field <c>file</c>, a JPEG, PNG or WebP up to 15 MB.
    /// </summary>
    [HttpPost("watermark-check")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public Task<WatermarkCheckResponse> CheckWatermark(IFormFile file, CancellationToken cancellationToken)
        => content.CheckWatermarkAsync(file.OpenReadStream(), cancellationToken);

    /// <summary>The countries a project's or office's place is chosen from (GeoNames).</summary>
    [HttpGet("places/countries")]
    [HasAnyPermission(PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage)]
    public IReadOnlyList<WebsiteCountryResponse> Countries() => content.Countries();

    /// <summary>A country's cities, biggest first, narrowed by what was typed: at most 50.</summary>
    [HttpGet("places/cities")]
    [HasAnyPermission(PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage)]
    public IReadOnlyList<WebsiteCityResponse> Cities([FromQuery] string country, [FromQuery] string? search) => content.Cities(country, search);

    /// <summary>
    /// Cities of that name in other countries, for a city not found in the one chosen ("Brazzaville is in Congo -
    /// Brazzaville"): at most five, from three letters typed.
    /// </summary>
    [HttpGet("places/cities-elsewhere")]
    [HasAnyPermission(PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage)]
    public IReadOnlyList<WebsiteCityElsewhereResponse> CitiesElsewhere([FromQuery] string country, [FromQuery] string search)
        => content.CitiesElsewhere(country, search);

    /// <summary>Services, locations, social links, the contact email and the favicon.</summary>
    [HttpGet("content")]
    [HasAnyPermission(PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> Content(CancellationToken cancellationToken) => content.GetAsync(cancellationToken);

    [HttpPost("services")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> AddService(WebsiteServiceRequest request, CancellationToken cancellationToken)
        => content.AddServiceAsync(request, cancellationToken);

    [HttpPut("services/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> UpdateService(long id, WebsiteServiceRequest request, CancellationToken cancellationToken)
        => content.UpdateServiceAsync(id, request, cancellationToken);

    [HttpDelete("services/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> DeleteService(long id, CancellationToken cancellationToken)
        => content.DeleteServiceAsync(id, cancellationToken);

    [HttpPut("services/order")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> OrderServices(WebsiteOrderRequest request, CancellationToken cancellationToken)
        => content.OrderServicesAsync(request, cancellationToken);

    [HttpPost("locations")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> AddLocation(WebsiteLocationRequest request, CancellationToken cancellationToken)
        => content.AddLocationAsync(request, cancellationToken);

    [HttpPut("locations/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> UpdateLocation(long id, WebsiteLocationRequest request, CancellationToken cancellationToken)
        => content.UpdateLocationAsync(id, request, cancellationToken);

    [HttpDelete("locations/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> DeleteLocation(long id, CancellationToken cancellationToken)
        => content.DeleteLocationAsync(id, cancellationToken);

    [HttpPut("locations/order")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> OrderLocations(WebsiteOrderRequest request, CancellationToken cancellationToken)
        => content.OrderLocationsAsync(request, cancellationToken);

    [HttpPost("social-links")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> AddSocialLink(WebsiteSocialLinkRequest request, CancellationToken cancellationToken)
        => content.AddSocialLinkAsync(request, cancellationToken);

    [HttpPut("social-links/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> UpdateSocialLink(long id, WebsiteSocialLinkRequest request, CancellationToken cancellationToken)
        => content.UpdateSocialLinkAsync(id, request, cancellationToken);

    [HttpDelete("social-links/{id:long}")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> DeleteSocialLink(long id, CancellationToken cancellationToken)
        => content.DeleteSocialLinkAsync(id, cancellationToken);

    [HttpPut("social-links/order")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> OrderSocialLinks(WebsiteOrderRequest request, CancellationToken cancellationToken)
        => content.OrderSocialLinksAsync(request, cancellationToken);

    /// <summary>
    /// Puts the website under construction (it then shows only its construction page, Contact and Privacy), or brings it
    /// back. The public settings say so within the minute they are cached for.
    /// </summary>
    [HttpPut("under-construction")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> SetUnderConstruction(WebsiteUnderConstructionRequest request, CancellationToken cancellationToken)
        => content.SetUnderConstructionAsync(request, cancellationToken);

    /// <summary>The website's favicon, from the caller's upload (PNG, JPEG or WebP).</summary>
    [HttpPut("favicon")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> SetFavicon(AttachFileRequest request, CancellationToken cancellationToken)
        => content.SetFaviconAsync(request, cancellationToken);

    [HttpDelete("favicon")]
    [HasPermission(PermissionCodes.WebsiteManage)]
    public Task<WebsiteContentResponse> RemoveFavicon(CancellationToken cancellationToken) => content.RemoveFaviconAsync(cancellationToken);
}
