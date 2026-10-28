using ID_Indetails.api.Business.Finance;

namespace ID_Indetails.api.Repositories.Finance.Abstraction;

/// <summary>Everything a project's money is calculated from, loaded for many projects at once (one query per source).</summary>
public interface IProjectFinanceRepository
{
    Task<IReadOnlyList<ExchangeRateTable.Rate>> ExchangeRatesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkEntryRow>> WorkEntriesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectFinance.PayRecord>> PayHistoryAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseRow>> ExpensesAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialRow>> MaterialsAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroundLabourRow>> GroundLabourAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Live payments of one project: the day each was received and its USD value.</summary>
    Task<IReadOnlyList<(DateOnly ReceivedOn, decimal AmountUsd)>> PaymentsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Live payments per project, in USD.</summary>
    Task<IReadOnlyDictionary<long, decimal>> ReceivedAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Per project, in USD: what was moved into the pocket money, and what the pocket paid out to it.</summary>
    Task<IReadOnlyDictionary<long, (decimal ToPocket, decimal FromPocket)>> PocketAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>One project's pocket movements, as it sees them: when (UTC) and the USD it gained (a payout) or lost (a contribution).</summary>
    Task<IReadOnlyList<(DateTime At, decimal AmountUsd)>> PocketMovesAsync(long projectId, CancellationToken cancellationToken = default);
}

public sealed record WorkEntryRow(long ProjectId, long UserId, DateTime StartedAt, int Minutes);

public sealed record ExpenseRow(long ProjectId, string Currency, decimal Amount, DateOnly SpentOn);

/// <param name="SentAt">When a client statement listing it was first sent (any delivery); null until then.</param>
public sealed record MaterialRow(long ProjectId, string Currency, decimal Total, decimal? ClientPrice, DateTime AddedAt, DateTime? SentAt);

public sealed record GroundLabourRow(long ProjectId, DateOnly WorkDate, decimal Cost);
