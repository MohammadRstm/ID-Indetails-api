using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// Staff arranging a Design project's client portal draft (order, hero, introduction), previewing it, and publishing
/// it to clients or going back to what they see. Each change answers with the whole content page.
/// </summary>
public interface IPortalPublishingService
{
    Task<PortalContentResponse> ReorderAsync(long projectId, PortalSection section, PortalOrderRequest request, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> SetHeroAsync(long projectId, PortalHeroRequest request, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> ClearHeroAsync(long projectId, CancellationToken cancellationToken = default);

    Task<PortalContentResponse> SetSummaryAsync(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Undoes a removal: the item is back in the draft.</summary>
    Task<PortalContentResponse> RestoreAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>Makes the draft what clients see, all at once, if it is still the revision the person saw.</summary>
    Task<PortalContentResponse> PublishAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Puts the draft back to what clients see; files never published are deleted.</summary>
    Task<PortalContentResponse> DiscardAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>The draft as the client would see it.</summary>
    Task<PortalPresentationResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default);
}
