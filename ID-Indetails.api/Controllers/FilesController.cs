using ID_Indetails.api.Business.Files;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>The multipart field <c>file</c>. Uploads attached to nothing within a day are deleted.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(FileRules.MaxVideoBytes + EnvelopeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileRules.MaxVideoBytes + EnvelopeBytes)]
    public async Task<ActionResult<FileResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return StatusCode(StatusCodes.Status201Created, await fileService.UploadAsync(content, file.FileName, file.Length, cancellationToken));
    }

    /// <summary>The content, shown inline. Videos can be read in ranges.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken)
    {
        var file = await fileService.OpenAsync(id, cancellationToken);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = file.Name }.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.MimeType, enableRangeProcessing: true);
    }
}
