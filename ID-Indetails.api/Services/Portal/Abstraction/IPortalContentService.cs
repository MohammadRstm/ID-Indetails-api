using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// A Design project's files (its Project files): uploads straight to the content store, the items that show the files,
/// their order, and which of them are chosen for the client. Clients see nothing of this until someone publishes the
/// client portal (<see cref="IPortalPublishingService"/>).
/// </summary>
public interface IPortalContentService
{
    Task<ProjectFilesResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Starts sending one file. The same client token again gives the same upload.</summary>
    Task<PortalUploadStartedResponse> StartUploadAsync(long projectId, StartPortalUploadRequest request, CancellationToken cancellationToken = default);

    Task<PortalPartLinksResponse> PartLinksAsync(long projectId, long uploadId, PortalPartLinksRequest request, CancellationToken cancellationToken = default);

    /// <summary>Joins and checks the parts, then adds the file to its section (or swaps it in for the replaced item's). Safe to repeat.</summary>
    Task<ProjectFileUploadCompletedResponse> CompleteUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default);

    /// <summary>Stops an upload and drops what reached the store. Safe to repeat.</summary>
    Task CancelUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default);

    Task<ProjectFileResponse> UpdateItemAsync(long projectId, long itemId, UpdatePortalItemRequest request, CancellationToken cancellationToken = default);

    Task<PortalUsageChangedResponse> DeleteItemAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>Undoes a deletion clients haven't seen yet: the item is back.</summary>
    Task<ProjectFilesResponse> RestoreAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    Task<ProjectFilesResponse> ReorderAsync(long projectId, PortalSection section, PortalOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Chooses a file for the client, or takes it back; clients see the change once it is published.</summary>
    Task<ProjectFileResponse> SetForClientAsync(long projectId, long itemId, ProjectFileForClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>A short-lived link to open (images, videos, PDFs) or download one file.</summary>
    Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default);

    /// <summary>
    /// One file's bytes, for Proton to read: to whoever may open its link, and only when it is no larger than
    /// <paramref name="maxBytes"/> (otherwise <see cref="ProjectFileBytes.Data"/> is null and nothing is read).
    /// </summary>
    Task<ProjectFileBytes> ReadAsync(long projectId, long itemId, long maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>A project file as read whole: its title, name, type and size, and its bytes when they were small enough to read.</summary>
public sealed record ProjectFileBytes(long ItemId, string Title, string FileName, string ContentType, long Size, byte[]? Data);
