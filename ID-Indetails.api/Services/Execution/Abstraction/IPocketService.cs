using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>
/// Execution's pocket money: super admins move part of a site's money in by hand, and sites that run short ask for
/// money back. Everything here needs <c>financial:view</c> and the Execution domain.
/// </summary>
public interface IPocketService
{
    Task<PocketResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<PagedResponse<PocketEntryResponse>> EntriesAsync(PocketEntryListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Moves part of a site's money into the pocket, never so much that its profit reaches zero.</summary>
    Task<PocketResponse> ContributeAsync(PocketContributionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks the pocket for money for a site. A site waiting for a decision already gets that request back.</summary>
    Task<PocketRequestResponse> RequestAsync(CreatePocketRequestRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for the system, within the caller's transaction: no actor, and only when the site has no request
    /// waiting. Super admins who see Execution's money are told.
    /// </summary>
    Task RaiseAsync(Project site, string? note, CancellationToken cancellationToken = default);

    /// <summary>Pays the chosen amount out to the site, once; at most what the pocket holds.</summary>
    Task<PocketRequestResponse> ApproveAsync(long requestId, ApprovePocketRequestRequest request, CancellationToken cancellationToken = default);

    Task<PocketRequestResponse> DenyAsync(long requestId, DenyPocketRequestRequest request, CancellationToken cancellationToken = default);
}
