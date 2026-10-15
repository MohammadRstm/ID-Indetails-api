using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// What the staff content services share: which project a staff member may work on, and how files, the client portal
/// and presentations are described (with their short-lived links). The client's page and the staff preview are built
/// by the same method.
/// </summary>
public interface IPortalContentViews
{
    /// <summary>
    /// A Design project whose client portal the caller may look at: they hold client_portal:manage or
    /// client_access:manage. Without either, 403; anything else reads as not found.
    /// </summary>
    Task<PortalContentProject> VisibleProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A Design project whose client portal the caller may change: client_portal:manage. Otherwise as <see cref="VisibleProjectAsync"/>.</summary>
    Task<PortalContentProject> ManagedProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A Design project whose files the caller may see (project:view), and what they may do with them.</summary>
    Task<ProjectFilesAccess> FilesAccessAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Everything the client portal tab shows: the client-relevant items with their states, usage, hero, introduction and publication.</summary>
    Task<PortalContentResponse> ContentAsync(PortalContentProject project, CancellationToken cancellationToken = default);

    /// <summary>Everything the Project files tab shows: every item, with what the caller may do with it.</summary>
    Task<ProjectFilesResponse> FilesAsync(ProjectFilesAccess access, CancellationToken cancellationToken = default);

    Task<ProjectFileResponse> FileAsync(ProjectFilesAccess access, long itemId, CancellationToken cancellationToken = default);

    /// <summary>The presentation as a client sees it: the published copy, or (<paramref name="published"/> false) the draft.</summary>
    Task<PortalPresentationResponse> PresentationAsync(PortalContentProject project, bool published, CancellationToken cancellationToken = default);

    /// <summary>A short-lived link to open (images, videos, PDFs, when <paramref name="inline"/>) or download one file. 503 without a store.</summary>
    Task<PortalContentLinkResponse> LinkAsync(PortalFileView file, bool inline, CancellationToken cancellationToken = default);
}
