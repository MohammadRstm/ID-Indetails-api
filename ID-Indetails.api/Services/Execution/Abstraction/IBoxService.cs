using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>
/// The Execution box: client payments of its projects go in, money goes out to projects in the red, and a closed
/// project's revenue is taken out or kept. The page needs <c>financial:view</c> and the Execution domain.
/// </summary>
public interface IBoxService
{
    Task<BoxResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<PagedResponse<BoxEntryResponse>> EntriesAsync(BoxEntryListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Moves money to a project in the red, never beyond the balance or what brings the project to zero.</summary>
    Task<BoxResponse> TransferAsync(BoxTransferRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes or keeps the money of a project closed by someone who can't see money.</summary>
    Task<BoxResponse> DecideAsync(long projectId, BoxDecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Within the caller's transaction: a project's new payment goes in while it takes payments.</summary>
    void PaymentReceived(Project project, Payment payment, long actorId);

    /// <summary>Within the caller's transaction: a voided payment the box holds is given back.</summary>
    Task PaymentVoidedAsync(Payment payment, long actorId, CancellationToken cancellationToken = default);

    /// <summary>Within the caller's transaction (the project tracked): an Execution project completed, or archived while open.</summary>
    Task ClosedAsync(Project project, BoxChoice? choice, UserAccess actor, CancellationToken cancellationToken = default);

    /// <summary>Within the caller's transaction (the project tracked): an Execution project reopened.</summary>
    Task ReopenedAsync(Project project, BoxChoice? choice, UserAccess actor, CancellationToken cancellationToken = default);
}
