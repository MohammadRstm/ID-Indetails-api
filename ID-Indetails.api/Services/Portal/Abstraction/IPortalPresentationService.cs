using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// A client reading a project's published presentation. Only an account with live access to the project gets
/// anything; everything else (no access, access ended, another project, a file not published) reads as not found.
/// </summary>
public interface IPortalPresentationService
{
    Task<PortalPresentationResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A short-lived link to open or download one published file.</summary>
    Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default);
}
