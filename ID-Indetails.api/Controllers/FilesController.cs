using ID_Indetails.api.Business.Files;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace ID_Indetails.api.Controllers;

/// <summary>Photos and videos. Upload first, then attach the returned id; see docs/files.md.</summary>
[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController(IFileService fileService) : ControllerBase
{
    /// <summary>Room for the multipart envelope around the largest video.</summary>
    private const long EnvelopeBytes = 1024 * 1024;

    /// <summary>
    /// The multipart field <c>file</c>, read as it arrives rather than buffered first, so it is written once. Uploads
    /// attached to nothing within a day are deleted.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(FileRules.MaxVideoBytes + EnvelopeBytes)]
    [Consumes("multipart/form-data")]
    [DisableFormBinding]
    public async Task<ActionResult<FileResponse>> Upload(CancellationToken cancellationToken)
    {
        var boundary = MediaTypeHeaderValue.TryParse(Request.ContentType, out var type) ? HeaderUtilities.RemoveQuotes(type.Boundary).Value : null;
        if (string.IsNullOrEmpty(boundary))
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "file", "Send the file as multipart/form-data, in the field \"file\".");
        }

        var reader = new MultipartReader(boundary, Request.Body);
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                && disposition.IsFileDisposition()
                && HeaderUtilities.RemoveQuotes(disposition.Name).Value == "file")
            {
                var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                return StatusCode(StatusCodes.Status201Created,
                    await fileService.UploadAsync(section.Body, name, Request.ContentLength ?? 0, cancellationToken));
            }
        }

        throw new RequestValidationException(ErrorCodes.ValidationFailed, "file", "Choose a file to upload.");
    }

    /// <summary>
    /// The content, shown inline. Videos can be read in ranges. <c>?size=</c> asks for a photo's smaller copy (its
    /// longest side, rounded up to 160, 480, 960 or 1920 px), for previews.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Download(long id, [FromQuery] int? size, CancellationToken cancellationToken)
    {
        var file = await fileService.OpenAsync(id, size, cancellationToken);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = file.Name }.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.MimeType, enableRangeProcessing: true);
    }
}
