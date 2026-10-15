using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// What the staff content services share: which project a staff member may manage, and how content and presentations
/// are described (with their short-lived links). The client's page and the staff preview are built by the same method.
/// </summary>
public interface IPortalContentViews
{
    /// <summary>A Design project the caller manages client portal content on. Anything else reads as not found.</summary>
    Task<PortalContentProject> VisibleProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Everything the staff content page shows: items with their states, usage, hero, introduction and publication.</summary>
    Task<PortalContentResponse> ContentAsync(PortalContentProject project, CancellationToken cancellationToken = default);

    Task<PortalContentItemResponse> ItemAsync(PortalItemView item, long? heroFileId, CancellationToken cancellationToken = default);

    /// <summary>The presentation as a client sees it: the published copy, or (<paramref name="published"/> false) the draft.</summary>
    Task<PortalPresentationResponse> PresentationAsync(PortalContentProject project, bool published, CancellationToken cancellationToken = default);
}
