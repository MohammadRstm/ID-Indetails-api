using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// Staff looking at a Design project's client portal (the files chosen for the client, with what clients see), and
/// those holding client_portal:manage arranging its draft (hero, introduction), previewing it, and publishing it to
/// clients or going back to what they see. Each change answers with the whole client portal page.
/// </summary>
public interface IPortalPublishingService
{
    Task<PortalContentResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A short-lived link to open or download one of the client-relevant files.</summary>
    Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> SetHeroAsync(long projectId, PortalHeroRequest request, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> ClearHeroAsync(long projectId, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> SetSummaryAsync(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Makes the draft what clients see, all at once, if it is still the revision the person saw.</summary>
    Task<PortalContentResponse> PublishAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Puts the client choices, hero and introduction back to what clients see. The files themselves stay as they are.</summary>
    Task<PortalContentResponse> DiscardAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>The draft as the client would see it.</summary>
    Task<PortalPresentationResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default);
}
