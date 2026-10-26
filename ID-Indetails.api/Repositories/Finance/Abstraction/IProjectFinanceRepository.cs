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

    /// <summary>Money the Execution box moved to each project, in USD.</summary>
    Task<IReadOnlyDictionary<long, decimal>> FromBoxAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Money the box moved to one project: when each move was made (UTC) and its USD value.</summary>
    Task<IReadOnlyList<(DateTime MovedAt, decimal AmountUsd)>> BoxMovesAsync(long projectId, CancellationToken cancellationToken = default);
}

public sealed record WorkEntryRow(long ProjectId, long UserId, DateTime StartedAt, int Minutes);

public sealed record ExpenseRow(long ProjectId, string Currency, decimal Amount, DateOnly SpentOn);

/// <param name="SentAt">When a client statement listing it was first sent (any delivery); null until then.</param>
public sealed record MaterialRow(long ProjectId, string Currency, decimal Total, decimal? ClientPrice, DateTime AddedAt, DateTime? SentAt);

public sealed record GroundLabourRow(long ProjectId, DateOnly WorkDate, decimal Cost);
