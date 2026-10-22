using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// Staff looking at a Design project's client portal (the files chosen for the client, with what clients see), and
/// those holding client_portal:manage arranging its draft (hero images, introduction), previewing it, and publishing it
/// to clients or going back to what they see. Each change answers with the whole client portal page.
/// </summary>
public interface IPortalPublishingService
{
    Task<PortalContentResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A short-lived link to open or download one of the client-relevant files.</summary>
    Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default);

    /// <summary>Adds an image item's file to the end of the draft hero images: once each, up to 12.</summary>
    Task<PortalContentResponse> AddHeroAsync(long projectId, PortalHeroRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes a file off the draft hero images.</summary>
    Task<PortalContentResponse> RemoveHeroAsync(long projectId, long fileId, CancellationToken cancellationToken = default);

    /// <summary>Puts the draft hero images in a new order, given as exactly their files.</summary>
    Task<PortalContentResponse> OrderHeroesAsync(long projectId, PortalHeroOrderRequest request, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> SetSummaryAsync(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Makes the draft what clients see, all at once, if it is still the revision the person saw.</summary>
    Task<PortalContentResponse> PublishAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Puts the client choices, hero images and introduction back to what clients see. The files themselves stay as they are.</summary>
    Task<PortalContentResponse> DiscardAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>The draft as the client would see it.</summary>
    Task<PortalPresentationResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// When the project moves to another client: nothing published for the previous client stays published, and from
    /// <paramref name="at"/> on clients see only the discussion written after it. The drafts stay, for staff to publish
    /// again. Staged in the caller's unit of work and transaction; files nothing uses any more are let go and deleted by
    /// the content cleanup.
    /// </summary>
    Task WithdrawForNewClientAsync(long projectId, DateTime at, CancellationToken cancellationToken = default);
}
