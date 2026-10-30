using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Execution;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>
/// Supervision taken: the company withdrawing some of an Execution site's supervision fee from the project account.
/// Not a cost, so it never changes the site's profit.
/// </summary>
public interface ISupervisionWithdrawalService
{
    /// <summary>The site's withdrawals that were not removed, the latest day first. Needs <c>financial:view</c>.</summary>
    Task<IReadOnlyList<SupervisionWithdrawalResponse>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Records one, while the site is open. Needs <c>payment:record</c>.</summary>
    Task<SupervisionWithdrawalResponse> TakeAsync(long projectId, TakeSupervisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes one (it stays on record, marked removed), while the site is open. Needs <c>payment:record</c>.</summary>
    Task RemoveAsync(long withdrawalId, CancellationToken cancellationToken = default);
}
