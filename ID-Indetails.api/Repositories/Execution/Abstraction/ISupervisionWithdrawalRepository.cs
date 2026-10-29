using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Execution.Abstraction;

public sealed record SupervisionWithdrawalRow(SupervisionWithdrawal Withdrawal, string CreatedByName);

public interface ISupervisionWithdrawalRepository
{
    /// <summary>The project's withdrawals that were not removed, the latest day first. Not tracked.</summary>
    Task<IReadOnlyList<SupervisionWithdrawalRow>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, removed ones included.</summary>
    Task<SupervisionWithdrawalRow?> GetRowAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>A withdrawal that was not removed, tracked and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends.</summary>
    Task<SupervisionWithdrawal?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>What the project's withdrawals that were not removed add up to, in USD.</summary>
    Task<decimal> TakenAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(SupervisionWithdrawal withdrawal);
}
