using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// Staff preparing a Design project's client portal content: uploads straight to the content store, then the items that
/// show the files. Everything stays a draft no client can reach.
/// </summary>
public interface IPortalContentService
{
    Task<PortalContentResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Starts sending one file. The same client token again gives the same upload.</summary>
    Task<PortalUploadStartedResponse> StartUploadAsync(long projectId, StartPortalUploadRequest request, CancellationToken cancellationToken = default);

    Task<PortalPartLinksResponse> PartLinksAsync(long projectId, long uploadId, PortalPartLinksRequest request, CancellationToken cancellationToken = default);

    /// <summary>Joins and checks the parts, then adds the file to its section (or swaps it in for the replaced item's). Safe to repeat.</summary>
    Task<PortalUploadCompletedResponse> CompleteUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default);

    /// <summary>Stops an upload and drops what reached the store. Safe to repeat.</summary>
    Task CancelUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default);

    Task<PortalContentItemResponse> UpdateItemAsync(long projectId, long itemId, UpdatePortalItemRequest request, CancellationToken cancellationToken = default);

    Task<PortalUsageChangedResponse> DeleteItemAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>A short-lived link to open (images, videos, PDFs) or download one file.</summary>
    Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default);
}
