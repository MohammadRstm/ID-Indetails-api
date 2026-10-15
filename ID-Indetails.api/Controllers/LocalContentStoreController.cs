using ID_Indetails.api.Services.ContentStore.Implementation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The links of the local content store, standing in for R2's presigned links (docs/content-storage.md). The signed token
/// in the link is the authorization, as with a presigned URL. Without the local store (R2, or none) every action answers 404.
/// </summary>
[ApiController]
[Route("api/content-store")]
[AllowAnonymous]
// Development and test store only: no staff token rides these requests, so the global per-IP limit would 429 uploads; the signed token is the control.
[DisableRateLimiting]
public sealed class LocalContentStoreController(LocalContentStore? store = null) : ControllerBase
{
    /// <summary>One part of an upload: the raw bytes, with nothing else. The token's maximum is enforced by the store.</summary>
    [HttpPut("parts/{token}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> PutPart(string token, CancellationToken cancellationToken)
    {
        var part = store is null ? null : await store.WritePartAsync(token, Request.Body, Request.ContentLength, cancellationToken);
        if (part is null)
        {
            return NotFound();
        }

        Response.Headers.ETag = part.ETag;
        return Ok();
    }

    /// <summary>The file a download link points at, in ranges if asked.</summary>
    [HttpGet("objects/{token}")]
    public IActionResult Download(string token)
    {
        var download = store?.ResolveDownload(token);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue(download.Inline ? "inline" : "attachment")
        {
            FileNameStar = download.FileName,
        }.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(download.Path, download.ContentType, enableRangeProcessing: true);
    }
}
